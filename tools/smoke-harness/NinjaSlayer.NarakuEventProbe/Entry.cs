using Godot;
using MegaCrit.Sts2.Core.AutoSlay.Helpers;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

[ModInitializer(nameof(Init))]
public static class NarakuEventProbe
{
    static string output = "";
    static readonly BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null)!;
    public static void Init()
    {
        output = CommandLineHelper.GetValue("naraku-output") ?? "";
        if (output.Length == 0) return;
        Directory.CreateDirectory(output);
        DenyTelemetry();
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.NoFocus, true);
        DisplayServer.WindowSetPosition(new(-5000, -5000));
        _ = Run();
    }
    static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void DenyTelemetry()
    {
        var registry = FindType("STS2RitsuLib.Telemetry.TelemetryRegistry");
        var setter = FindType("STS2RitsuLib.RitsuLibFramework").GetMethods(Static).Single(m => m.Name == "SetTelemetryApplicantConsent");
        var deny = Enum.Parse(FindType("STS2RitsuLib.Telemetry.TelemetryConsentState"), "Denied");
        foreach (var applicant in (IEnumerable)registry.GetMethod("GetApplicants", Static)!.Invoke(null, null)!)
        {
            var ps = setter.GetParameters(); var args = new object?[ps.Length];
            args[0] = applicant.GetType().GetProperty("ApplicantId")!.GetValue(applicant); args[1] = deny;
            for (int i = 2; i < args.Length; i++) args[i] = ps[i].DefaultValue;
            setter.Invoke(null, args);
        }
        FindType("NinjaSlayer.Content.NinjaSlayerTelemetryConsent").GetMethod("SetEnabled", Static)!.Invoke(null, [false]);
    }
    static async Task Run()
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        try
        {
            async Task Frame() => await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            async Task Frames(int count) { for (int i = 0; i < count; i++) await Frame(); }
            async Task Wait(Func<bool> condition, string message)
            {
                ulong start = Time.GetTicksMsec();
                while (!condition()) { Require(Time.GetTicksMsec() - start < 40000, message); await Frame(); }
            }
            async Task<Image> Capture(string? name = null)
            {
                await tree.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                var image = tree.Root.GetTexture().GetImage(); image.Convert(Image.Format.Rgba8);
                if (name != null) image.SavePng(Path.Combine(output, name + ".png"));
                return image;
            }
            NEventOptionButton[] Options() => NEventRoom.Instance == null ? [] :
                UiHelper.FindAll<NEventOptionButton>(NEventRoom.Instance).Where(b => b.IsEnabled && b.IsVisibleInTree()).ToArray();
            VideoStreamPlayer Video() => UiHelper.FindAll<VideoStreamPlayer>(NEventRoom.Instance!).Single();
            await Wait(() => NGame.Instance?.MainMenu != null, "Main menu unavailable");
            await Frames(180); DenyTelemetry(); SaveManager.Instance.SetFtuesEnabled(false);
            var game = NGame.Instance!;
            var character = ModelDb.AllCharacters.Single(c => c.Id.Entry.Contains("NINJA", StringComparison.OrdinalIgnoreCase));
            await game.StartNewSingleplayerRun(character, false,
                [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()], [], "NARAKUFILM", GameMode.Standard);
            await Frames(120);
            var model = (EventModel)typeof(ModelDb).GetMethod("Event", Static)!
                .MakeGenericMethod(FindType("NinjaSlayer.Events.NarakuEvent")).Invoke(null, null)!;
            await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: model);
            NMapScreen.Instance?.Close(false);
            await Wait(() => Options().Length == 2, "Naraku options absent");
            await Frames(150);
            FindType("STS2RitsuLib.Ui.Toast.RitsuToastService").GetMethod("CloseAll", Static)!.Invoke(null, [true]);
            var video = Video(); ulong id = video.GetInstanceId();
            Require(video.Loop && video.Volume == 0 && video.IsPlaying(), "Silent looping player is not active");
            Require(video.MouseFilter == Control.MouseFilterEnum.Ignore, "Film blocks input");
            var portrait = NEventRoom.Instance!.Layout!.GetNode<TextureRect>("Portrait");
            Require(portrait.GetChildCount() == 1, "Duplicate portrait VFX");
            var transform = video.GetGlobalTransformWithCanvas();
            var filmSize = video.Size;
            using (await Capture("event-initial-real-ui")) { }
            var info = new ProcessStartInfo("ffmpeg") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
            foreach (var arg in new[] { "-y", "-v", "error", "-f", "rawvideo", "-pix_fmt", "rgba", "-s", "1920x1080", "-r", "24", "-i", "pipe:0", "-an", "-c:v", "libx264", "-preset", "fast", "-crf", "17", "-threads", "8", "-pix_fmt", "yuv420p", "-movflags", "+faststart", Path.Combine(output, "event-real-ui.mp4") }) info.ArgumentList.Add(arg);
            int wraps = 0; double previous = video.StreamPosition;
            using (var encoder = Process.Start(info)!)
            {
                ulong start = Time.GetTicksMsec();
                int frames = 0;
                while (wraps < 10)
                {
                    Require(Time.GetTicksMsec() - start < 40000, "Ten video loops timed out");
                    await Frame(); double position = video.StreamPosition;
                    if (position + .3 < previous) wraps++;
                    previous = position;
                    Require(video.IsPlaying(), "Playback stopped between loops");
                    if (frames < 168 && Time.GetTicksMsec() - start >= (ulong)(frames * 1000 / 24))
                    {
                        using var image = await Capture(); encoder.StandardInput.BaseStream.Write(image.GetData()); frames++;
                    }
                }
                encoder.StandardInput.Close(); encoder.WaitForExit(); Require(encoder.ExitCode == 0, "UI recording failed");
            }
            var player = LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState())!;
            int hp = player.Creature.CurrentHp;
            int damage = 0;
            for (int i = 0; i < 3; i++)
            {
                await UiHelper.Click(Options()[0]); await Frames(90); damage += 5 + i;
                Require(player.Creature.CurrentHp == hp - damage, "Event damage changed");
                Require(Video().GetInstanceId() == id && Video().IsPlaying(), "Dialog page restarted/replaced video");
                using (await Capture("event-call-" + (i + 1))) { }
            }
            await UiHelper.Click(Options()[0]); await Frames(90);
            Require(player.Relics.Any(r => r.GetType().Name == "NarakuWithinRelic"), "Naraku reward missing");
            using (await Capture("event-accepted")) { }
            await RunManager.Instance.EnterRoomDebug(RoomType.RestSite); await Frames(60);
            Require(!GodotObject.IsInstanceValid(video), "Video survived event exit");
            await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: model); await Frames(120);
            Require(Video().IsPlaying() && Video().GetInstanceId() != id, "Event re-entry failed");
            await UiHelper.Click(Options()[1]); await Frames(90);
            using (await Capture("event-silenced")) { }
            File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
            {
                status = "pass", loop_wraps = wraps, initial_options = 2, damage_sequence = new[] { 5, 6, 7 },
                page_player_identity_preserved = true, reward_obtained = true, exit_freed = true,
                reentry_playing = true, silent = true, mouse_passthrough = true,
                video_origin = new[] { transform.Origin.X, transform.Origin.Y },
                video_size = new[] { filmSize.X, filmSize.Y }, installed_game_modified = false
            }, new JsonSerializerOptions { WriteIndented = true }));
            await game.ReturnToMainMenu(); await Frames(120); game.Quit();
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(output, "error.txt"), ex.ToString());
            tree.Root.GetTexture().GetImage().SavePng(Path.Combine(output, "failure.png")); tree.Quit(2);
        }
    }
}
