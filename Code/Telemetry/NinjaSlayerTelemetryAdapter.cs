using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using STS2RitsuLib.Telemetry;

namespace NinjaSlayer.Code.Telemetry;

// RitsuLib owns consent, persistence and retries. Its built-in PostHog adapter
// sends up to 1,000 queued events as one uncompressed request.
internal sealed class NinjaSlayerTelemetryAdapter(Uri endpoint) : ITelemetryAdapter
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(60) };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    // The framework acknowledges a whole flush. Remember successful individual
    // sends until then so a failed report or rate limit does not restart the batch.
    private readonly HashSet<(string Event, string Request, DateTimeOffset Timestamp)> _accepted = [];
    public string AdapterId => "ninja_slayer_gzip";
    public string EndpointDescription => endpoint.ToString();

    public async ValueTask<TelemetrySendResult> SendAsync(TelemetryApplicant applicant,
        IReadOnlyList<TelemetryEnvelope> events, CancellationToken cancellationToken = default)
    {
        _accepted.IntersectWith(events.Select(e => (e.EventName, e.RequestId, e.TimestampUtc)));
        string? failure = null;
        // A rejected public report must not prevent balance records from arriving.
        foreach (TelemetryEnvelope evt in events.OrderBy(e => e.EventName == "battle_report.completed"))
        {
            var key = (evt.EventName, evt.RequestId, evt.TimestampUtc);
            if (_accepted.Contains(key)) continue;
            var properties = new Dictionary<string, object?>(evt.Properties, StringComparer.OrdinalIgnoreCase)
            {
                ["schema"] = evt.Schema, ["applicant_id"] = evt.ApplicantId,
                ["request_id"] = evt.RequestId, ["category"] = evt.Category.ToString()
            };
            if (evt.Payload is not null) properties["payload"] = evt.Payload;
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(new
            {
                api_key = "proxy",
                batch = new[] { new { @event = evt.EventName,
                    distinct_id = properties["anonymous_install_id"], properties, timestamp = evt.TimestampUtc } }
            }, JsonOptions);
            using var compressed = new MemoryStream();
            using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
                gzip.Write(json);
            using var content = new ByteArrayContent(compressed.ToArray());
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            content.Headers.ContentEncoding.Add("gzip");
            try
            {
                using var response = await Client.PostAsync(endpoint, content, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    _accepted.Add(key);
                    continue;
                }
                string reason = response.ReasonPhrase ?? "Upload rejected";
                // Our receiver returns short machine-readable errors; never log a response payload.
                try
                {
                    using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    if (error.RootElement.TryGetProperty("error", out var code) && code.ValueKind == JsonValueKind.String)
                        reason = code.GetString()!;
                }
                catch (JsonException) { } // A proxy can return an HTML error instead of the receiver's JSON.
                failure = $"{(int)response.StatusCode} {reason}; {evt.EventName}: {json.Length} JSON bytes, {compressed.Length} gzip bytes";
                if (response.StatusCode is not (HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge))
                    return TelemetrySendResult.Fail(failure);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return TelemetrySendResult.Fail($"Timed out uploading {evt.EventName} ({compressed.Length} gzip bytes).");
            }
            catch (HttpRequestException error)
            {
                return TelemetrySendResult.Fail(error.Message);
            }
        }
        if (failure is not null) return TelemetrySendResult.Fail(failure);
        _accepted.Clear();
        return TelemetrySendResult.Ok();
    }
}
