using System.Net;
using System.Net.Sockets;
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
            for (int attempt = 0; attempt < 3; attempt++)
            {
                HttpListenerContext context = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(15));
                using var bytes = new MemoryStream();
                await context.Request.InputStream.CopyToAsync(bytes);
                requests.Add(new { path = context.Request.RawUrl, method = context.Request.HttpMethod,
                    contentType = context.Request.ContentType, body = Convert.ToBase64String(bytes.ToArray()),
                    submissionId = context.Request.Headers["X-NinjaSlayer-Submission-Id"] });
                context.Response.StatusCode = attempt == 0 ? 503 : 200;
                byte[] response = Encoding.UTF8.GetBytes(attempt == 0 ? "{\"error\":\"storage_busy\"}" :
                    $"{{\"ok\":true,\"id\":\"{id}\"}}");
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
        await receive;
        string? directory = System.Environment.GetEnvironmentVariable("NINJASLAYER_UPLOAD_FIXTURE_DIR");
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "requests.json"), JsonSerializer.Serialize(requests));
        }
        GD.Print("PASS candidate DLL feedback multipart, receipt, retry and native PostHog HTTP requests");
    }
}
