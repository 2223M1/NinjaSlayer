using Godot;
using MegaCrit.Sts2.Core.AutoSlay.Helpers;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using System.Collections;
using System.Reflection;
using System.Text.Json;

[ModInitializer(nameof(Init))]
public static class FixedEventMusicProbe
{
    static string output = "";
    const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    static Type Find(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null)!;
    static readonly string[][] Tracks = [
        ["YukanoEvent", "YukanoEventMusicGuid", "yukano_teahouse"],
        ["YamotoKokiCuteEvent", "YamotoKokiEventMusicGuid", "yamoto_koki_remix"],
        ["NarakuEvent", "NarakuEventMusicGuid", "naraku_crystal_variation"],
    ];
    public static void Init()
    {
        output = CommandLineHelper.GetValue("fixed-music-output") ?? "";
        if (output.Length == 0) return;
        Directory.CreateDirectory(output);
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.NoFocus, true);
        DisplayServer.WindowSetPosition(new(-5000, -5000));
        _ = Run();
    }
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void DenyTelemetry()
    {
        var registry = Find("STS2RitsuLib.Telemetry.TelemetryRegistry");
        var setter = Find("STS2RitsuLib.RitsuLibFramework").GetMethods(Static).Single(m => m.Name == "SetTelemetryApplicantConsent");
        var denied = Enum.Parse(Find("STS2RitsuLib.Telemetry.TelemetryConsentState"), "Denied");
        foreach (var applicant in (IEnumerable)registry.GetMethod("GetApplicants", Static)!.Invoke(null, null)!)
        {
            var ps = setter.GetParameters(); var args = new object?[ps.Length];
            args[0] = applicant.GetType().GetProperty("ApplicantId")!.GetValue(applicant); args[1] = denied;
            for (int i = 2; i < args.Length; i++) args[i] = ps[i].DefaultValue;
            setter.Invoke(null, args);
        }
        Find("NinjaSlayer.Content.NinjaSlayerTelemetryConsent").GetMethod("SetEnabled", Static)!.Invoke(null, [false]);
    }
    static GodotObject Description(string field)
    {
        var id = Find("NinjaSlayer.Content.NinjaSlayerAudio").GetField(field)!.GetRawConstantValue();
        return (GodotObject)Find("STS2RitsuLib.Audio.FmodStudioServer")
            .GetMethod("TryGetEventDescriptionFromGuid", Static)!.Invoke(null, [id])!;
    }
    static GodotObject[] Active(string field) => Description(field).Call("get_instance_list").AsGodotArray()
        .Select(x => x.AsGodotObject()).Where(x => x.Call("get_playback_state").AsInt32() != 2).ToArray();
    static NEventOptionButton[] Options() => NEventRoom.Instance == null ? [] :
        UiHelper.FindAll<NEventOptionButton>(NEventRoom.Instance).Where(b => b.IsEnabled && b.IsVisibleInTree()).ToArray();
    static EventModel Model(string name) => (EventModel)typeof(ModelDb).GetMethod("Event", Static)!
        .MakeGenericMethod(Find("NinjaSlayer.Events." + name)).Invoke(null, null)!;
    static async Task Run()
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        var results = new List<object>();
        try
        {
            async Task Frames(int count) { for (int i = 0; i < count; i++) await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame); }
            async Task Wait(Func<bool> condition, string message)
            {
                ulong start = Time.GetTicksMsec();
                while (!condition()) { Require(Time.GetTicksMsec() - start < 45000, message); await Frames(1); }
            }
            await Wait(() => NGame.Instance?.MainMenu != null, "Menu unavailable");
            await Frames(120); DenyTelemetry(); SaveManager.Instance.SetFtuesEnabled(false);
            var game = NGame.Instance!;
            var character = ModelDb.AllCharacters.Single(c => c.Id.Entry.Contains("NINJA", StringComparison.OrdinalIgnoreCase));
            await game.StartNewSingleplayerRun(character, false,
                [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()], [], "FIXEDEVENTMUSIC", GameMode.Standard);
            await Frames(100);
            foreach (var row in Tracks)
            {
                await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: Model(row[0]));
                NMapScreen.Instance?.Close(false);
                await Wait(() => Options().Length == 2 && Active(row[1]).Length == 1, row[0] + " did not start");
                var instance = Active(row[1]).Single();
                ulong id = instance.GetInstanceId();
                Require(instance.Call("get_parameter_by_name", "event_end").AsSingle() == 0, "Started in Outro");
                var vanilla = NRunMusicController.Instance!.GetNode<Node>("Proxy").Get("_musicEv").AsGodotObject();
                Require(vanilla == null || !GodotObject.IsInstanceValid(vanilla) || vanilla.Call("get_playback_state").AsInt32() == 2,
                    "Vanilla act music is playing over event music");
                await Frames(90);
                if (row[0] == "NarakuEvent")
                {
                    await UiHelper.Click(Options()[0]); await Frames(60);
                    Require(Active(row[1]).Single().GetInstanceId() == id, "Dialog page restarted music");
                    Require(instance.Call("get_parameter_by_name", "event_end").AsSingle() == 0, "Intermediate page ended music");
                }
                await UiHelper.Click(Options()[1]);
                await Wait(() => instance.Call("get_parameter_by_name", "event_end").AsSingle() == 1, "Event completion did not request Outro");
                await Wait(() => Active(row[1]).Length == 0, "Outro failed to stop naturally");
                await Frames(30);
                vanilla = NRunMusicController.Instance!.GetNode<Node>("Proxy").Get("_musicEv").AsGodotObject();
                Require(vanilla != null && vanilla.Call("get_playback_state").AsInt32() != 2, "Act music did not resume");
                await RunManager.Instance.EnterRoomDebug(RoomType.RestSite); await Frames(30);
                await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: Model(row[0]));
                NMapScreen.Instance?.Close(false);
                await Wait(() => Active(row[1]).Length == 1, "Re-entry failed");
                Require(Active(row[1])[0].GetInstanceId() != id, "Re-entry reused old instance");
                await RunManager.Instance.EnterRoomDebug(RoomType.RestSite); await Frames(60);
                Require(Active(row[1]).Length == 0, "Early room exit leaked music");
                results.Add(new { model = row[0], one_instance = true, no_layered_act_music = true,
                    natural_outro = true, act_restored = true, early_exit_clean = true, reentry = true });
                File.WriteAllText(Path.Combine(output, "progress.json"), JsonSerializer.Serialize(results));
            }
            await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: Model("YukanoEvent"));
            await Frames(60); await game.ReturnToMainMenu(); await Frames(60);
            Require(Tracks.All(r => Active(r[1]).Length == 0), "Music survived return to menu");
            File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = true, results, menu_cleanup = true }, new JsonSerializerOptions { WriteIndented = true }));
            game.Quit();
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(output, "error.txt"), ex.ToString());
            tree.Quit(2);
        }
    }
}
