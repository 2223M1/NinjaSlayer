using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.FeedbackScreen;
using NinjaSlayer.Code.Feedback;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private static Uri? _feedbackLoopback;

    private async Task VerifyFeedbackUploadAsync()
    {
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        _feedbackLoopback = new Uri($"http://127.0.0.1:{port}/feedback");
        var harmony = new Harmony("ninjaslayer.smoke.feedback-loopback");
        var send = AccessTools.Method(typeof(System.Net.Http.HttpClient), "SendAsync",
            [typeof(HttpRequestMessage), typeof(HttpCompletionOption), typeof(CancellationToken)]);
        harmony.Patch(send, prefix: new HarmonyMethod(typeof(SmokeController), nameof(RouteFeedbackToLoopback)));
        try
        {
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.F2, Pressed = true });
            await WaitFrames(3);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.F2, Pressed = false });
            var screen = NGame.Instance!.GetOrCreateFeedbackScreen();
            Require(screen.Visible && NinjaSlayerFeedbackSession.TryGetCurrentToken(screen.GetInstanceId(), out _),
                "Native F2 did not bind the NinjaSlayer feedback session.");
            NinjaSlayerFeedbackSession.TryGetCurrentToken(screen.GetInstanceId(), out var token);
            Require(NinjaSlayerFeedbackSession.TryConfirm(token), "Could not confirm the isolated feedback session.");
            Task<string> receive = Task.Run(async () =>
            {
                var request = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(15));
                using var bytes = new MemoryStream();
                await request.Request.InputStream.CopyToAsync(bytes);
                string id = request.Request.Headers["X-NinjaSlayer-Submission-Id"]!;
                Require(Guid.TryParse(id, out _), "F2 send did not reach the NinjaSlayer feedback client.");
                byte[] reply = Encoding.UTF8.GetBytes($"{{\"ok\":true,\"id\":\"{id}\"}}");
                request.Response.ContentType = "application/json";
                request.Response.ContentLength64 = reply.Length;
                await request.Response.OutputStream.WriteAsync(reply);
                request.Response.Close();
                return id;
            });
            using var screenshot = new MemoryStream(NGame.Instance.GetViewport().GetTexture().GetImage().SavePngToBuffer());
            byte[] zip = new byte[22]; zip[0] = 80; zip[1] = 75; zip[2] = 5; zip[3] = 6;
            using var logs = new MemoryStream(zip);
            var data = new FeedbackData { description = "Isolated F2 upload contract", category = "bug", gameVersion = "contract" };
            var task = (Task<bool>)AccessTools.Method(typeof(NSendFeedbackScreen), "SendFeedback")
                .Invoke(null, [data, screenshot, logs])!;
            Require(await task, "Native feedback send did not accept the NinjaSlayer receipt.");
            Require(!screenshot.CanRead && !logs.CanRead, "F2 send did not close its upload streams.");
            _checkpoints.Write("feedback.f2-upload", data: new JsonObject { ["submissionId"] = await receive,
                ["nativeF2"] = true, ["matchingReceipt"] = true, ["destination"] = "loopback" });
            AccessTools.Method(typeof(NSendFeedbackScreen), "Close").Invoke(screen, null);
            Require(!NinjaSlayerFeedbackSession.IsCurrent(token), "Closing F2 left a routed feedback session.");
        }
        finally { harmony.UnpatchAll(harmony.Id); _feedbackLoopback = null; }
    }

    private static void RouteFeedbackToLoopback(HttpRequestMessage request)
    {
        if (request.RequestUri?.Host == "ninja-slayer-telemetry.theonetrue2223.workers.dev"
            && request.RequestUri.AbsolutePath == "/feedback") request.RequestUri = _feedbackLoopback!;
    }
}
