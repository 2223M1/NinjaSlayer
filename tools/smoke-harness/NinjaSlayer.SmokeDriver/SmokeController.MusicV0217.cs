using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.AutoSlay.Helpers;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using NinjaSlayer.Content;
using NinjaSlayer.Events;
using NinjaSlayer.Monsters;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private async Task VerifyMusicV0217Live(string directory, CancellationToken ct)
    {
        var run = RunManager.Instance.DebugOnlyGetState()!;
        var router = new Harmony("NinjaSlayer.SmokeDriver.MusicReload");
        router.Patch(AccessTools.Method(typeof(RunManager), "CreateRoom"),
            prefix: new HarmonyMethod(typeof(V0217MusicRoomFixture), nameof(V0217MusicRoomFixture.Prefix)));
        V0217MusicRoomFixture.Event = ModelDb.Event<SawatariEvent>();
        Type route = typeof(SawatariEvent).Assembly.GetType("NinjaSlayer.Code.Patches.SawatariEventRoute", true)!;
        AccessTools.Method(route, "Schedule").Invoke(null, [run.Act, ModelDb.Encounter<GremlinMercNormal>()]);
        Require((bool)AccessTools.Method(route, "TryActivate").Invoke(null, [run.Act])!, "Sawatari music fixture did not activate.");
        await RunManager.Instance.EnterMapPointInternal(run.ActFloor, run.CurrentMapPoint!.PointType, null, saveGame: true);
        await Ready();
        await CheckMusic("sawatari-fresh", NinjaSlayerAudio.SawatariCoopMusicEvent, NinjaSlayerAudio.SawatariCoopPhaseParameter);
        await Reload();
        await CheckMusic("sawatari-reloaded", NinjaSlayerAudio.SawatariCoopMusicEvent, NinjaSlayerAudio.SawatariCoopPhaseParameter);
        var state = CombatManager.Instance.DebugOnlyGetState()!;
        Require(state.Creatures.Any(c => c.Monster is SawatariMonster && c.Side == CombatSide.Player),
            "Sawatari ally must survive event reconstruction.");
        while (state.HittableEnemies.Any()) await CreatureCmd.Kill(state.HittableEnemies.ToArray(), force: true);
        await CombatManager.Instance.CheckWinCondition();
        await WaitUntilAsync(() => GetSawatariOptions().Count == 2 && GetSawatariOptions().All(o => o.IsEnabled), "Sawatari decision missing.", ct);
        await CheckMusic("sawatari-decision", NinjaSlayerAudio.SawatariCoopMusicEvent, NinjaSlayerAudio.SawatariCoopPhaseParameter);
        await UiHelper.Click(GetSawatariOptions()[1]);
        await WaitFrames(12);
        await CheckMusic("sawatari-duel-immediate", NinjaSlayerAudio.SawatariCoopMusicEvent, NinjaSlayerAudio.SawatariCoopPhaseParameter);
        await WaitUntilAsync(() => !CombatManager.Instance.IsPaused && state.HittableEnemies.Any(c => c.Monster is SawatariMonster), "Duel not started.", ct);
        await CreatureCmd.Kill(state.HittableEnemies.ToArray(), force: true);
        await WaitUntilAsync(() => GetSawatariOptions().Count == 1 && GetSawatariOptions()[0].IsEnabled, "Duel result missing.", ct);
        await CheckMusic("sawatari-result", NinjaSlayerAudio.SawatariCoopMusicEvent, NinjaSlayerAudio.SawatariCoopPhaseParameter);
        await UiHelper.Click(GetSawatariOptions()[0]);
        await CloseRewards();
        _checkpoints.Write("v0217.live-sawatari-music-reload-and-phases");

        run = RunManager.Instance.DebugOnlyGetState()!;
        V0217MusicRoomFixture.Event = ModelDb.Event<DarkNinjaEvent>();
        await RunManager.Instance.EnterMapPointInternal(run.ActFloor, run.CurrentMapPoint!.PointType, null, saveGame: true);
        await WaitUntilAsync(() => Options().Length == 2, "Dark Ninja initial options missing.", ct);
        await UiHelper.Click(Options()[1]);
        await WaitUntilAsync(() => Options().Length == 1, "Dark Ninja fight option missing.", ct);
        await UiHelper.Click(Options()[0]);
        await Ready();
        await CheckMusic("dark-fresh", NinjaSlayerAudio.DarkNinjaBattleMusicEvent, NinjaSlayerAudio.DarkNinjaProgressParameter);
        await Reload();
        await WaitUntilAsync(() => Options().Length == 2, "Reloaded Dark Ninja event options missing.", ct);
        await UiHelper.Click(Options()[1]);
        await WaitUntilAsync(() => Options().Length == 1, "Reloaded fight option missing.", ct);
        await UiHelper.Click(Options()[0]);
        await Ready();
        await CheckMusic("dark-reloaded", NinjaSlayerAudio.DarkNinjaBattleMusicEvent, NinjaSlayerAudio.DarkNinjaProgressParameter);
        await CreatureCmd.Kill(CombatManager.Instance.DebugOnlyGetState()!.HittableEnemies.ToArray(), force: true);
        await CombatManager.Instance.CheckWinCondition();
        await CloseRewards();
        await WaitFrames(90);
        _tree.Root.GetTexture().GetImage().SavePng(Path.Combine(directory, "dark-victory-art.png"));
        Type musicProxy = typeof(STS2RitsuLib.Audio.FmodStudioServer).Assembly.GetType("STS2RitsuLib.Audio.Internal.GuidMappedNaudioStudioProxy", true)!;
        await WaitUntilAsync(() => AccessTools.Field(musicProxy, "_runMusicInstance").GetValue(null) == null,
            "Dark Ninja outro did not finish and restore room music.", ct);
        _checkpoints.Write("v0217.live-dark-outro-completed");
        V0217MusicRoomFixture.Event = null;
        router.UnpatchAll(router.Id);
        await RunManager.Instance.EnterRoomDebug(RoomType.RestSite);
        await WaitFrames(60);
        string path = NRunMusicController.Instance!.GetNode<Node>("Proxy").Get("_currentTrack").AsString();
        Require(AccessTools.Field(musicProxy, "_runMusicInstance").GetValue(null) == null && !string.IsNullOrEmpty(path),
            "Custom event music leaked into the next room.");
        _checkpoints.Write("v0217.live-dark-music-reload-and-cleanup", data: new JsonObject { ["restMusic"] = path });
        foreach (var model in new EventModel[] { ModelDb.Event<YamotoKokiCuteEvent>(), ModelDb.Event<YukanoEvent>() })
        {
            await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: model);
            await WaitFrames(60);
            _tree.Root.GetTexture().GetImage().SavePng(Path.Combine(directory, model.GetType().Name + "-art.png"));
        }
        var settings = STS2RitsuLib.Settings.ModSettingsNavigator.RequestOpenByIds("NinjaSlayer", null, null, null);
        Require(settings.Success, "Native settings navigation failed: " + settings.Message);
        await WaitFrames(90);
        _tree.Root.GetTexture().GetImage().SavePng(Path.Combine(directory, "narration-settings.png"));
        _checkpoints.Write("v0217.live-narration-settings-page");
        _checkpoints.Write("music-v0217.completed");

        async Task Ready()
        {
            await WaitUntilAsync(() => CombatManager.Instance.IsInProgress && !CombatManager.Instance.IsStarting
                && LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState())?.PlayerCombatState?.Phase == PlayerTurnPhase.Play,
                "Event did not reach play phase.", ct);
            await RunManager.Instance.ActionExecutor.FinishedExecutingActions();
            NMapScreen.Instance?.Close(animateOut: false);
            await WaitFrames(60);
        }
        async Task Reload()
        {
            await NGame.Instance!.ReturnToMainMenu();
            await WaitUntilAsync(() => NGame.Instance?.MainMenu != null, "Main menu not ready.", ct);
            var button = NGame.Instance!.MainMenu!.GetNode<NButton>("MainMenuTextButtons/ContinueButton");
            await WaitUntilAsync(() => button.Visible && button.IsEnabled, "Native Continue not ready.", ct);
            await UiHelper.Click(button);
            await WaitUntilAsync(() => RunManager.Instance.IsInProgress && NEventRoom.Instance != null, "Event reload did not finish.", ct);
            // Continue is asynchronous; wait for its fade-in before emitting event clicks.
            await WaitFrames(180);
            if (V0217MusicRoomFixture.Event is SawatariEvent) await Ready();
        }
        async Task CheckMusic(string label, string expected, string parameter)
        {
            var proxy = NRunMusicController.Instance!.GetNode<Node>("Proxy");
            Type mapped = typeof(STS2RitsuLib.Audio.FmodStudioServer).Assembly.GetType("STS2RitsuLib.Audio.Internal.GuidMappedNaudioStudioProxy", true)!;
            string? path = (string?)AccessTools.Field(mapped, "_runMusicPath").GetValue(null);
            var instance = (GodotObject?)AccessTools.Field(mapped, "_runMusicInstance").GetValue(null);
            if (label == "sawatari-fresh")
            {
                var description = STS2RitsuLib.Audio.FmodStudioServer.TryGetEventDescriptionFromGuid("{4dc577b9-8347-4b1e-b6c1-e4e2e5847207}")!;
                File.WriteAllLines(Path.Combine(directory, "event-description-methods.txt"), description.GetMethodList().Select(m => m["name"].ToString()));
            }
            Require(path == expected && instance != null && instance.Call("get_playback_state").AsInt32() != 2,
                $"{label}: expected active {expected}, actual {path}.");
            _checkpoints.Write("v0217.music." + label, data: new JsonObject {
                ["track"] = path, ["instance"] = instance!.GetInstanceId(),
                ["parameter"] = instance.Call("get_parameter_by_name", parameter).ToString(),
                ["timeline"] = instance.Call("get_timeline_position").ToString()
            });
            if (parameter == NinjaSlayerAudio.SawatariCoopPhaseParameter)
                Require(instance.Call("get_parameter_by_name", parameter).AsSingle() == (label.Contains("decision") ? 1 : label.Contains("duel") ? 3 : label.Contains("result") ? 4 : 0),
                    "Native phase parameter did not update immediately.");
            await WaitFrames(30);
            _tree.Root.GetTexture().GetImage().SavePng(Path.Combine(directory, label + ".png"));
        }
        async Task CloseRewards()
        {
            await WaitUntilAsync(() => NOverlayStack.Instance?.Peek() is NRewardsScreen, "Manual rewards missing.", ct);
            await UiHelper.Click(UiHelper.FindFirst<NProceedButton>((NRewardsScreen)NOverlayStack.Instance!.Peek()!)!);
            await WaitUntilAsync(() => NOverlayStack.Instance?.Peek() is not NRewardsScreen || NMapScreen.Instance?.IsOpen == true,
                "Rewards did not proceed to the event or map.", ct);
        }
        static NEventOptionButton[] Options() => NEventRoom.Instance == null ? []
            : UiHelper.FindAll<NEventOptionButton>(NEventRoom.Instance).Where(b => b.IsEnabled && b.IsVisibleInTree()).ToArray();
    }
}

// Fix only the encounter choice so both initial entry and native Continue replay the
// same event. Saving, loading, event reconstruction and all music paths are unchanged.
internal static class V0217MusicRoomFixture
{
    internal static EventModel? Event;
    public static void Prefix(ref RoomType roomType, ref AbstractModel? model)
    {
        if (Event == null) return;
        roomType = RoomType.Event;
        model = Event;
        if (Event is SawatariEvent)
        {
            Type route = typeof(SawatariEvent).Assembly.GetType("NinjaSlayer.Code.Patches.SawatariEventRoute", true)!;
            var act = RunManager.Instance.DebugOnlyGetState()!.Act;
            AccessTools.Method(route, "Schedule").Invoke(null, [act, ModelDb.Encounter<GremlinMercNormal>()]);
            AccessTools.Method(route, "TryActivate").Invoke(null, [act]);
        }
    }
}
