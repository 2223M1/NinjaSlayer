using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.FeedbackScreen;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Content;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.Feedback;

public static class NinjaSlayerFeedbackClient
{
    private const string FeedbackUrl = "https://ninja-slayer-telemetry.theonetrue2223.workers.dev/feedback";
    private const int MaxAttempts = 3;
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(60) };
    private static readonly int[] RetryDelaysMs = [1500, 2500];

    public static async Task<bool> SendAsync(FeedbackData data, Stream screenshotStream, Stream logsStream)
    {
        string submissionId = Guid.NewGuid().ToString("D");
        string submittedAtUtc = DateTimeOffset.UtcNow.ToString("O");

        try
        {
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                try
                {
                    screenshotStream.Position = 0;
                    logsStream.Position = 0;
                    using MultipartFormDataContent form = BuildMultipartContent(
                        data,
                        BuildModContext(submissionId, submittedAtUtc),
                        screenshotStream,
                        logsStream);
                    using HttpRequestMessage request = new(HttpMethod.Put, FeedbackUrl) { Content = form };
                    request.Headers.TryAddWithoutValidation("X-NinjaSlayer-Feedback-Version", "1");
                    using HttpResponseMessage response = await HttpClient.SendAsync(request);

                    if (response.IsSuccessStatusCode)
                    {
                        Entry.Logger.Info($"NinjaSlayer feedback {submissionId} uploaded successfully.");
                        return true;
                    }

                    string responseBody = await response.Content.ReadAsStringAsync();
                    int statusCode = (int)response.StatusCode;
                    Entry.Logger.Warn(
                        $"NinjaSlayer feedback attempt {attempt + 1}/{MaxAttempts} rejected " +
                        $"({response.StatusCode}): {responseBody}");
                    if (statusCode is >= 400 and < 500 && statusCode != 429)
                    {
                        return false;
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    Entry.Logger.Warn(
                        $"NinjaSlayer feedback attempt {attempt + 1}/{MaxAttempts} failed: {ex}");
                }

                if (attempt < MaxAttempts - 1)
                {
                    await Task.Delay(RetryDelaysMs[attempt]);
                }
            }

            Entry.Logger.Warn($"NinjaSlayer feedback {submissionId} failed after all retry attempts.");
            return false;
        }
        finally
        {
            screenshotStream.Close();
            logsStream.Close();
        }
    }

    private static MultipartFormDataContent BuildMultipartContent(
        FeedbackData data,
        object modContext,
        Stream screenshotStream,
        Stream logsStream)
    {
        var payload = new
        {
            data.description,
            data.category,
            data.gameVersion,
            data.commit,
            data.platformBranch,
            data.isModded,
            data.isFullConsole,
            data.lang
        };

        MultipartFormDataContent form = [];
        AddJson(form, "payload_json", payload);
        AddJson(form, "mod_context", modContext);
        AddStream(form, "screenshot", "screenshot.png", "image/png", screenshotStream);
        AddStream(form, "logs", "logs.zip", "application/zip", logsStream);
        return form;
    }

    private static object BuildModContext(string submissionId, string submittedAtUtc)
    {
        RunState? runState = RunManager.Instance.DebugOnlyGetState();
        var player = runState == null ? null : LocalContext.GetMe(runState);
        return new
        {
            submissionId,
            submittedAtUtc,
            modVersion = NinjaSlayerVersion.Current,
            characterId = player?.Character.Id.ToString() ?? "unknown",
            isDebugCharacter = player?.Character is NinjaSlayerDebugCharacter,
            seed = runState?.Rng.StringSeed,
            currentActIndex = runState?.CurrentActIndex,
            actId = runState?.Act.Id.ToString(),
            actFloor = runState?.ActFloor,
            totalFloor = runState?.TotalFloor,
            room = runState?.CurrentRoom?.Id.ToString(),
            roomType = runState?.CurrentRoom?.GetType().Name,
            ascensionLevel = runState?.AscensionLevel,
            gameMode = runState?.GameMode.ToString(),
            playerCount = runState?.Players.Count
        };
    }

    private static void AddJson(MultipartFormDataContent form, string name, object value)
    {
        StringContent content = new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
        form.Add(content, name);
    }

    private static void AddStream(
        MultipartFormDataContent form,
        string name,
        string fileName,
        string mediaType,
        Stream stream)
    {
        StreamContent content = new(new NonDisposingStream(stream));
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        form.Add(content, name, fileName);
    }

    private sealed class NonDisposingStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.WriteAsync(buffer, cancellationToken);
        protected override void Dispose(bool disposing) { }
    }
}
