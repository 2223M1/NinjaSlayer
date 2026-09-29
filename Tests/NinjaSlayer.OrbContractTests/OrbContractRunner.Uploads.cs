using System.Net;
using System.Net.Sockets;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.FeedbackScreen;
using NinjaSlayer.Code.Feedback;
using STS2RitsuLib.Telemetry;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static async Task VerifyUploadTransport()
    {
        const string id = "b2f0fc32-e9c4-4e06-a030-79b1c6f62f6b";
        using var portReservation = new TcpListener(IPAddress.Loopback, 0);
        portReservation.Start();
        int port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
        portReservation.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var requests = new List<object>();
        Task receive = Task.Run(async () =>
        {
            for (int attempt = 0; attempt < 9; attempt++)
            {
                HttpListenerContext context = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(15));
                using var bytes = new MemoryStream();
                await context.Request.InputStream.CopyToAsync(bytes);
                requests.Add(new { path = context.Request.RawUrl, method = context.Request.HttpMethod,
                    contentType = context.Request.ContentType, body = Convert.ToBase64String(bytes.ToArray()),
                    contentEncoding = context.Request.Headers["Content-Encoding"],
                    submissionId = context.Request.Headers["X-NinjaSlayer-Submission-Id"] });
                if (attempt >= 3)
                {
                    Require(context.Request.Headers["Content-Encoding"] == "gzip", "Telemetry must use HTTP gzip.");
                    bytes.Position = 0;
                    using var gzip = new GZipStream(bytes, CompressionMode.Decompress, leaveOpen: true);
                    var decoded = JsonNode.Parse(gzip)!;
                    Require(decoded["batch"]!.AsArray().Count == 1, "Queued records must be sent separately.");
                    if (attempt is 3 or 4 or 5 or 8)
                    {
                        var values = decoded["batch"]![0]!["properties"]!["payload"]!["applicant_payload"]!["measurements"]!.AsArray();
                        Require(values.Count == 2000 && values[1999]!["hp"]!.GetValue<int>() == 1999,
                            "Gzip must preserve every measurement.");
                        Require(bytes.Length < Encoding.UTF8.GetByteCount(decoded.ToJsonString()) / 5,
                            "Representative repetitive measurements should be substantially compressed.");
                    }
                }
                context.Response.StatusCode = attempt switch { 0 => 503, 4 => 429, 6 => 413, _ => 200 };
                byte[] response = Encoding.UTF8.GetBytes(attempt switch
                {
                    0 => "{\"error\":\"storage_busy\"}", 4 => "{\"error\":\"rate_limited\"}", 6 => "{\"error\":\"invalid_replay\"}",
                    _ =>
                    $"{{\"ok\":true,\"id\":\"{id}\"}}"
                });
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = response.Length;
                await context.Response.OutputStream.WriteAsync(response);
                context.Response.Close();
            }
        });
        var data = new FeedbackData { description = "本地反馈契约：中文与附件", category = "bug", gameVersion = "contract" };
        var modContext = new { submissionId = id, submittedAtUtc = "2026-09-15T00:00:00Z", modVersion = "contract", publishDescription = false };
        byte[] png = [137, 80, 78, 71, 13, 10, 26, 10];
        byte[] zip = new byte[22]; zip[0] = 80; zip[1] = 75; zip[2] = 5; zip[3] = 6;
        using var screenshot = new MemoryStream(png);
        using var logs = new MemoryStream(zip);
        using var http = new System.Net.Http.HttpClient();
        var transport = new FeedbackHttpClient(http,
            new FeedbackHttpClientOptions(new Uri($"http://127.0.0.1:{port}/feedback"),
                fallbackRetryDelays: [TimeSpan.Zero]), TimeProvider.System, SystemRetryDelayStrategy.Instance);
        var build = AccessTools.Method(typeof(NinjaSlayerFeedbackClient), "BuildMultipartContent");
        FeedbackSendResult result = await transport.SendAsync(id, endpoint =>
        {
            screenshot.Position = 0; logs.Position = 0;
            var form = (MultipartFormDataContent)build.Invoke(null, [data, modContext, screenshot, logs])!;
            var request = new HttpRequestMessage(HttpMethod.Put, endpoint) { Content = form };
            request.Headers.Add("X-NinjaSlayer-Submission-Id", id);
            return request;
        });
        Require(result.IsSuccess && result.Attempts.Count == 2, "Feedback must retry 503 and require its receipt.");
        Require(screenshot.CanRead && logs.CanRead, "Retry must preserve the caller's attachment streams.");
        var adapter = new PostHogTelemetryAdapter($"http://127.0.0.1:{port}", "proxy");
        var applicant = new TelemetryApplicant { ApplicantId = "NinjaSlayer", OwnerModId = "NinjaSlayer",
            DisplayName = "NinjaSlayer", Adapter = adapter, Requests = [] };
        var envelope = new TelemetryEnvelope { ApplicantId = "NinjaSlayer", EventName = "run_history.completed",
            RequestId = "balance_runs", Category = TelemetryDataCategory.RunHistory,
            Properties = new Dictionary<string, object?> { ["owner_mod_id"] = "NinjaSlayer",
                ["anonymous_install_id"] = "00000000000000000000000000000001" },
            Payload = new JsonObject { ["applicant_payload"] = new JsonObject() } };
        Require((await adapter.SendAsync(applicant, [envelope])).Success, "Native telemetry adapter failed loopback delivery.");
        var compressedAdapter = (ITelemetryAdapter)Activator.CreateInstance(
            typeof(NinjaSlayer.Content.NinjaSlayerBalanceTelemetry).Assembly.GetType("NinjaSlayer.Code.Telemetry.NinjaSlayerTelemetryAdapter", true)!,
            [new Uri($"http://127.0.0.1:{port}/batch/")])!;
        var measurements = new JsonArray(Enumerable.Range(0, 2000).Select(i => (JsonNode)new JsonObject
            { ["model"] = "CARD.NINJA_SLAYER_CHARACTER_STRIKE", ["hp"] = i, ["blocked"] = 0 }).ToArray());
        TelemetryEnvelope summary(int number) => new() { ApplicantId = "NinjaSlayer", EventName = "run_history.completed",
            RequestId = "balance_runs", Category = TelemetryDataCategory.RunHistory,
            TimestampUtc = DateTimeOffset.Parse("2026-09-15T00:00:00Z").AddSeconds(number),
            Properties = envelope.Properties, Payload = new JsonObject { ["applicant_payload"] = new JsonObject { ["measurements"] = measurements.DeepClone() } } };
        var report = JsonNode.Parse("""
            {"schema":"ninja_slayer_replay_v1","version":"0.3.4","won":true,"ascension":10,"mode":"Standard","party":1,
             "contributor":0,"reloads":0,"duration":60,"coverage":"complete",
             "floors":[{"floor":1,"rooms":[{"type":"Monster","model":"ENCOUNTER.TEST"}],"hp":50,"max_hp":50,"gold":99,"damage_taken":0,"healed":0}],
             "frames":[{"attempt":"cccccccccccccccccccccccccccccccc","floor":1,"room":0,"sequence":0,"action":{"kind":"combat_start","round":1,"side":"None"}},
                       {"attempt":"cccccccccccccccccccccccccccccccc","floor":1,"room":0,"sequence":1,"action":{"kind":"attempt_end","round":1,"side":"None"}}]}
            """)!;
        using var replayBytes = new MemoryStream();
        using (var gzip = new GZipStream(replayBytes, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(Encoding.UTF8.GetBytes(report.ToJsonString()));
        var replay = new TelemetryEnvelope { ApplicantId = "NinjaSlayer", EventName = "battle_report.completed",
            RequestId = "public_replays", Category = TelemetryDataCategory.RunHistory,
            Properties = new Dictionary<string, object?>(envelope.Properties) { ["game_version"] =
                Metadata(typeof(NinjaSlayer.Content.NinjaSlayerBalanceTelemetry).Assembly, "NinjaSlayerGameApiVersion") },
            Payload = new JsonObject { ["applicant_payload"] = new JsonObject { ["run_key"] = new string('a', 64),
                ["encoding"] = "gzip+base64", ["report"] = Convert.ToBase64String(replayBytes.ToArray()) } } };
        TelemetryEnvelope[] queued = [replay, summary(1), summary(2)];
        Require(!(await compressedAdapter.SendAsync(applicant, queued)).Success, "A partial 429 must keep the native queue.");
        // A later flush deserializes new envelope instances from the same native queue.
        queued = JsonSerializer.Deserialize<TelemetryEnvelope[]>(JsonSerializer.Serialize(queued))!;
        var rejected = await compressedAdapter.SendAsync(applicant, queued);
        Require(!rejected.Success && rejected.ErrorMessage!.Contains("413 invalid_replay", StringComparison.Ordinal),
            "Report rejection must remain retryable and identify the receiver's failure and sizes.");
        Require((await compressedAdapter.SendAsync(applicant, queued)).Success, "Only the unsent report should be retried.");
        Require((await compressedAdapter.SendAsync(applicant, [summary(1)])).Success, "A new flush must not inherit completed acknowledgements.");
        await receive;
        string? directory = System.Environment.GetEnvironmentVariable("NINJASLAYER_UPLOAD_FIXTURE_DIR");
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "requests.json"), JsonSerializer.Serialize(requests));
        }
        GD.Print("PASS candidate DLL feedback and gzip telemetry, individual delivery, partial retries and native queue envelopes");
    }
}
