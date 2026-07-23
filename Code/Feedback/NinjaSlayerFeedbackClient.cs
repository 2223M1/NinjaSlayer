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
    private const long MaxScreenshotBytes = 5L * 1024 * 1024;
    private const long MaxLogsBytes = 16L * 1024 * 1024;
    private static readonly HttpClient HttpClient = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly TimeProvider Clock = TimeProvider.System;
    private static readonly FeedbackHttpClient Transport = new(
        HttpClient,
        new FeedbackHttpClientOptions(new Uri(FeedbackUrl)),
        Clock,
        SystemRetryDelayStrategy.Instance);

    public static async Task<bool> SendAsync(
        FeedbackData data,
        Stream screenshotStream,
        Stream logsStream,
        CancellationToken cancellationToken = default)
    {
        string submissionId = Guid.NewGuid().ToString("D");
        string submittedAtUtc = Clock.GetUtcNow().ToString("O");

        if (!ValidateUploadStream(screenshotStream, MaxScreenshotBytes, "screenshot") ||
            !ValidateUploadStream(logsStream, MaxLogsBytes, "logs"))
        {
            return false;
        }

        object modContext = BuildModContext(submissionId, submittedAtUtc);
        FeedbackSendResult result = await Transport.SendAsync(
            endpoint =>
            {
                screenshotStream.Position = 0;
                logsStream.Position = 0;
                MultipartFormDataContent form = BuildMultipartContent(
                    data,
                    modContext,
                    screenshotStream,
                    logsStream);
                var request = new HttpRequestMessage(HttpMethod.Put, endpoint) { Content = form };
                request.Headers.TryAddWithoutValidation("X-NinjaSlayer-Feedback-Version", "1");
                request.Headers.TryAddWithoutValidation("X-NinjaSlayer-Submission-Id", submissionId);
                return request;
            },
            cancellationToken);

        LogResult(submissionId, result);
        return result.IsSuccess;
    }

    private static bool ValidateUploadStream(Stream stream, long maxBytes, string name)
    {
        if (!stream.CanRead || !stream.CanSeek)
        {
            Entry.Logger.Warn($"NinjaSlayer feedback {name} stream must be readable and seekable.");
            return false;
        }

        if (stream.Length <= maxBytes)
        {
            return true;
        }

        Entry.Logger.Warn(
            $"NinjaSlayer feedback {name} is {stream.Length} bytes; the limit is {maxBytes} bytes.");
        return false;
    }

    private static void LogResult(string submissionId, FeedbackSendResult result)
    {
        foreach (FeedbackAttemptDiagnostic diagnostic in result.Attempts)
        {
            if (diagnostic.Failure == FeedbackAttemptFailure.None)
            {
                continue;
            }

            string reason = diagnostic.StatusCode is { } statusCode
                ? $"{statusCode}: {diagnostic.Detail}"
                : $"{diagnostic.Failure}: {diagnostic.Detail}";
            Entry.Logger.Warn($"NinjaSlayer feedback attempt {diagnostic.Attempt} failed ({reason}).");
        }

        if (result.IsSuccess)
        {
            Entry.Logger.Info($"NinjaSlayer feedback {submissionId} uploaded successfully.");
            return;
        }

        Entry.Logger.Warn($"NinjaSlayer feedback {submissionId} ended with {result.Outcome}.");
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
        StreamContent content = new(new FeedbackNonDisposingStream(stream));
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        form.Add(content, name, fileName);
    }
}
