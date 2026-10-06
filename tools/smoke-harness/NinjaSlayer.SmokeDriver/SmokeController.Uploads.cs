using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using STS2RitsuLib.Settings;
using NinjaSlayer.Code.Feedback;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private static Uri? _feedbackLoopback;

    private async Task VerifyFeedbackUploadAsync()
    {
        // The form's in-memory attachment is sent only to loopback. No image is saved to disk.
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
            var screen = NinjaSlayerFeedbackScreen.Instance!;
            Require(screen is { Visible: true } && NGame.Instance!.FeedbackScreen is not { Visible: true },
                "F2 did not open only the independent NinjaSlayer feedback form.");
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
            var description = (NMegaTextEdit)screen.FindChild("DescriptionInput", true, false);
            description.InsertTextAtCaret("Isolated independent F2 upload contract");
            await WaitFrames(2);
            var button = (ModSettingsSidebarButton)screen.FindChild("SendButton", true, false);
            Require(!button.Disabled, "Nonempty feedback did not enable Send.");
            button.EmitSignal(BaseButton.SignalName.Pressed);
            Require(ReferenceEquals(NModalContainer.Instance!.OpenModal, screen) && button.Disabled,
                "One Send click must start uploading without another confirmation modal.");
            string submissionId = await receive;
            await WaitUntilAsync(() => description.Editable == false && button.Disabled
                && screen.FindChild("SendStatus", true, false).Get("text").AsString().Contains(
                    new MegaCrit.Sts2.Core.Localization.LocString("settings_ui", "FEEDBACK_SEND_SUCCESS_LABEL").GetFormattedText()),
                "Independent feedback did not display a successful receipt.");
            _checkpoints.Write("feedback.f2-upload", data: new JsonObject { ["submissionId"] = submissionId,
                ["independentF2"] = true, ["oneClick"] = true, ["matchingReceipt"] = true, ["destination"] = "loopback" });
            screen.Close();
            await WaitFrames(3);
            Require(NinjaSlayerFeedbackScreen.Instance is null && NModalContainer.Instance.OpenModal is null,
                "Closing F2 left a modal or feedback instance.");
            await VerifyIndependentFeedbackLanguagesAsync();
        }
        finally { harmony.UnpatchAll(harmony.Id); _feedbackLoopback = null; }
    }

    private async Task VerifyIndependentFeedbackLanguagesAsync()
    {
        var localization = MegaCrit.Sts2.Core.Localization.LocManager.Instance;
        string originalLanguage = localization.Language;
        try
        {
            foreach (string language in MegaCrit.Sts2.Core.Localization.LocManager.Languages)
            {
                localization.SetLanguage(language);
                Require(await NinjaSlayerFeedbackScreen.OpenAsync(), "Independent feedback failed to open in " + language);
                await WaitFrames(3);
                var screen = NinjaSlayerFeedbackScreen.Instance!;
                var description = (NMegaTextEdit)screen.FindChild("DescriptionInput", true, false);
                string expected = new MegaCrit.Sts2.Core.Localization.LocString("settings_ui", "NINJA_SLAYER_FEEDBACK_TITLE").GetFormattedText();
                Require(description.GetParent().GetChild(0).Get("text").AsString() == expected,
                    "Independent feedback title did not localize in " + language);
                var panel = description.GetParent().GetParent().GetParent<PanelContainer>();
                Vector2 viewport = screen.GetViewportRect().Size;
                Require(panel.Size.X <= viewport.X && panel.Size.Y <= viewport.Y,
                    "Independent feedback overflowed the viewport in " + language);
                _checkpoints.Write("feedback.language", data: new JsonObject { ["language"] = language, ["title"] = expected });
                screen.Close();
                await WaitFrames(3);
                Require(NModalContainer.Instance!.OpenModal is null && NinjaSlayerFeedbackScreen.Instance is null,
                    "Independent feedback retained modal ownership after " + language);
            }
        }
        finally { localization.SetLanguage(originalLanguage); }
    }

    private static void RouteFeedbackToLoopback(HttpRequestMessage request)
    {
        if (request.RequestUri?.Host == "telemetry.feixingwawa.cn"
            && request.RequestUri.AbsolutePath == "/feedback") request.RequestUri = _feedbackLoopback!;
    }
}
