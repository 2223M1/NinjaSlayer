using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using STS2RitsuLib.Data;
using STS2RitsuLib.Utils.Persistence;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private TheaterScript? _greetingScript;
    private TornadoViewportRecording? _greetingRecording;
    private bool _greetingReentry;
    private bool _greetingComplete;
    private long _greetingStart;
    private readonly JsonArray _greetingTimeline = [];
    private readonly JsonArray _greetingFrames = [];
    private string[]? _greetingInitialDraw;
    private string? _greetingInitialRng;
    private double GreetingSeconds => Stopwatch.GetElapsedTime(_greetingStart).TotalSeconds;

    internal void GreetingEvent(string name)
    {
        if (_greetingRecording == null) return;
        if (name is "relic-start" or "draw-start") Require(_greetingComplete, name + " ran before greeting completion.");
        _greetingTimeline.Add(new JsonObject { ["id"] = name, ["start"] = GreetingSeconds, ["end"] = GreetingSeconds });
    }

    internal bool TryRecordGreeting(ICombatState state, ref Task<bool> result)
    {
        if (_greetingScript == null || _greetingReentry) return false;
        result = RecordGreeting(state);
        return true;
    }

    private async Task<bool> RecordGreeting(ICombatState state)
    {
        string directory = _configuration.ActionPreviewDirectory!;
        _greetingRecording = new TornadoViewportRecording(_tree.Root, directory, "ultrafast", true);
        STS2RitsuLib.Ui.Toast.RitsuToastService.CloseAll(true);
        await _greetingRecording.Start();
        _greetingStart = _greetingRecording.StartTimestamp;
        _greetingInitialDraw = PileType.Draw.GetPile(state.Players[0]).Cards.Select(card => card.Id.ToString()).ToArray();
        _greetingInitialRng = System.Text.Json.JsonSerializer.Serialize(state.RunState.Rng.ToSerializable());
        RenderingServer.FramePreDraw += SampleGreeting;
        GreetingEvent("greeting-start");
        Task<bool> greeting;
        _greetingReentry = true;
        try { greeting = BossGreetingCinematic.TryPlay(state); }
        finally { _greetingReentry = false; }
        Task switching = _greetingScript!.GreetingMode.StartsWith("switch", StringComparison.Ordinal)
            ? SwitchGreeting(_greetingScript.GreetingMode == "switch-response") : Task.CompletedTask;
        Task pausing = _greetingScript.GreetingPause ? PauseGreeting() : Task.CompletedTask;
        bool played = await greeting;
        await switching;
        await pausing;
        Require(_greetingInitialDraw.SequenceEqual(PileType.Draw.GetPile(state.Players[0]).Cards.Select(card => card.Id.ToString())),
            "Greeting modified the initialized draw pile.");
        Require(_greetingInitialRng == System.Text.Json.JsonSerializer.Serialize(state.RunState.Rng.ToSerializable()),
            "Greeting consumed game RNG.");
        _greetingComplete = true;
        GreetingEvent("greeting-complete");
        return played;
    }

    private void SampleGreeting()
    {
        var node = NCombatRoom.Instance?.GetCreatureNode(RunManager.Instance.DebugOnlyGetState()!.Players[0].Creature);
        if (node == null) return;
        Node2D? body = node.Visuals.GetNodeOrNull<Node2D>("%Visuals");
        _greetingFrames.Add(new JsonObject { ["seconds"] = GreetingSeconds, ["rootX"] = node.Position.X,
            ["rootY"] = node.Position.Y, ["angle"] = body?.GetGlobalTransformWithCanvas().Rotation,
            ["scaleX"] = body?.GetGlobalTransformWithCanvas().Scale.X,
            ["scaleY"] = body?.GetGlobalTransformWithCanvas().Scale.Y,
            ["sceneX"] = NCombatRoom.Instance!.SceneContainer.Scale.X,
            ["hand"] = node.Entity.Player?.PlayerCombatState?.Hand.Cards.Count ?? 0 });
    }

    private async Task PauseGreeting()
    {
        await WaitFrames(6);
        NRun.Instance!.GlobalUi.SubmenuStack.ShowScreen(CapstoneSubmenuType.PauseMenu);
        await WaitFrames(2);
        GreetingEvent("pause-start");
        var node = NCombatRoom.Instance!.GetCreatureNode(RunManager.Instance.DebugOnlyGetState()!.Players[0].Creature)!;
        var pose = node.Visuals.GetNode<Node2D>("%AimPose");
        Transform2D held = pose.Transform;
        await WaitFrames(24);
        Require(pose.Transform.IsEqualApprox(held), "Brief bow progressed while the single-player pause menu was open.");
        GreetingEvent("pause-end");
        NRun.Instance.GlobalUi.CapstoneContainer.Close();
    }

    private async Task SwitchGreeting(bool afterResponse)
    {
        if (!afterResponse)
            await WaitUntilAsync(() => _tree.Root.FindChild("NinjaSlayerBossGreetingVideo", true, false) != null,
                "Full greeting did not start its video.");
        else
            await WaitUntilAsync(() => _greetingTimeline.OfType<JsonObject>().Any(row => row["id"]!.GetValue<string>() == "boss-response"),
                "Full greeting did not start boss response.");
        await WaitFrames(20);
        GreetingEvent("space-switch");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Space, PhysicalKeycode = Key.Space, Pressed = true });
        await WaitFrames(3);
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Space, PhysicalKeycode = Key.Space, Pressed = false });
    }

    private async Task RunGreetingPreview(TheaterScript script)
    {
        string directory = _configuration.ActionPreviewDirectory!;
        Directory.CreateDirectory(directory);
        _greetingScript = script;
        SaveManager.Instance.PrefsSave.FastMode = Enum.Parse<FastModeType>(script.GreetingSpeed);
        bool? initialPreference = script.GreetingMode == "brief" ? true
            : script.GreetingMode == "switch-response" ? false : null;
        var store = ModDataStore.For("NinjaSlayer");
        store.Modify<NinjaSlayerSettingsData>("ninja_slayer_settings",
            settings => settings.BriefBossGreetingEnabled = initialPreference);
        store.Save("ninja_slayer_settings");
        string settingsPath = ProjectSettings.GlobalizePath(
            ProfileManager.Instance.GetFilePath("settings.json", SaveScope.Global, "NinjaSlayer"));
        Require(JsonNode.Parse(File.ReadAllText(settingsPath))!["BriefBossGreetingEnabled"]?.GetValue<bool>() == initialPreference,
            "Initial greeting preference was not saved to disk.");
        var run = await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<NinjaSlayerCharacter>(),
            true, ActModel.GetDefaultList(), [], script.Seed, GameMode.Standard, 0);
        await RunManager.Instance.EnterAct(2);
        if (script.GreetingEncounter != null)
        {
            Type encounterType = typeof(EncounterModel).Assembly.GetTypes().Single(type => type.Name == script.GreetingEncounter);
            var encounter = (EncounterModel)AccessTools.Method(typeof(ModelDb), "Encounter", [], [encounterType]).Invoke(null, null)!;
            run.Act.SetBossEncounter(encounter);
        }
        Player player = run.Players.Single();
        await RelicCmd.Obtain<Pantograph>(player);
        player.Creature.SetCurrentHpInternal(20);
        await RunManager.Instance.EnterMapCoord(run.Map.BossMapPoint.coord);
        await WaitUntilAsync(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play,
            "Greeting did not release the player turn.", timeout: TimeSpan.FromMinutes(2));
        Require(player.Creature.CurrentHp == 45, "Pantograph did not heal once after greeting.");
        GreetingEvent("player-turn");
        JsonObject openingSnapshot = BossSnapshot(player);
        Require(_greetingTimeline.Count(row => row!["id"]!.GetValue<string>() == "relic-start") == 1,
            "Opening relic hook did not run exactly once.");
        // Real attacks provide source waveforms for measured AV alignment.
        for (int index = 0; index < 3; index++)
        {
            var state = CombatManager.Instance.DebugOnlyGetState()!;
            var card = state.CreateCard<KarateStraightRedesignV1>(player);
            await CardPileCmd.Add(card, PileType.Hand);
            await PlayerCmd.SetEnergy(3, player);
            var action = new PlayCardAction(card, state.HittableEnemies.First());
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(action);
            await action.CompletionTask;
            if (action.Exception != null) throw action.Exception;
            await WaitFrames(35);
        }
        await WaitFrames(60);
        Require((bool)AccessTools.Property(typeof(NinjaSlayerSettings), "BriefBossGreetingEnabled").GetValue(null)!
            == (script.GreetingMode != "switch-response"), "Greeting changed a manual choice or lost the automatic default.");
        Require(JsonNode.Parse(File.ReadAllText(settingsPath))!["BriefBossGreetingEnabled"]?.GetValue<bool>()
            == (script.GreetingMode != "switch-response"), "Greeting preference was not persisted to disk.");
        _checkpoints.Write("greeting-preview.preference-persisted", data: new JsonObject
        { ["initialPreference"] = initialPreference, ["savedPreference"] = script.GreetingMode != "switch-response" });
        RenderingServer.FramePreDraw -= SampleGreeting;
        await _greetingRecording!.Stop();
        File.Copy(_configuration.TheaterScriptPath!, Path.Combine(directory, "script.json"), true);
        File.WriteAllText(Path.Combine(directory, "timeline.json"), _greetingTimeline.ToJsonString());
        File.WriteAllText(Path.Combine(directory, "motion.json"), _greetingFrames.ToJsonString());
        File.WriteAllText(Path.Combine(directory, "damage.json"), "[]");
        File.WriteAllText(Path.Combine(directory, "coverage.json"), "{}");
        File.WriteAllText(Path.Combine(directory, "runtime.json"), new JsonObject
        { ["act"] = 3, ["mode"] = script.GreetingSpeed, ["fromCue"] = script.Cues[0].Id, ["toCue"] = script.Cues[^1].Id,
            ["captureStartSeconds"] = 0, ["rehearsal"] = false,
            ["drawBeforeGreeting"] = new JsonArray(_greetingInitialDraw!.Select(id => JsonValue.Create(id)).ToArray()),
            ["handAfterOpening"] = new JsonArray(PileType.Hand.GetPile(player).Cards.Select(card => JsonValue.Create(card.Id.ToString())).ToArray())
        }.ToJsonString());
        _checkpoints.Write("greeting-preview.complete");
        _greetingScript = null;
        _greetingRecording = null;
        int greetingsBeforeReload = _bossGreetings;
        await NGame.Instance.ReturnToMainMenu();
        RunState resumed = await ResumeBossCheckpoint();
        Player restored = resumed.Players.Single();
        await WaitUntilAsync(() => restored.PlayerCombatState?.Phase == PlayerTurnPhase.Play,
            "Reload did not release combat.");
        Require(_bossGreetings == greetingsBeforeReload, "Reload replayed the completed boss greeting.");
        Require(JsonNode.DeepEquals(openingSnapshot, BossSnapshot(restored)), "Reload changed opening HP or card order.");
        _checkpoints.Write("greeting-preview.reload-identical");
        NGame.Instance.Quit();
    }
}

[HarmonyPatch(typeof(Pantograph), nameof(Pantograph.BeforeCombatStart))]
internal static class GreetingRelicObserver
{
    private static void Prefix() => SmokeController.Current?.GreetingEvent("relic-start");
}

[HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.Draw), [typeof(PlayerChoiceContext), typeof(decimal), typeof(Player), typeof(bool)])]
internal static class GreetingDrawObserver
{
    private static void Prefix() => SmokeController.Current?.GreetingEvent("draw-start");
}

[HarmonyPatch]
internal static class GreetingResponseObserver
{
    private static MethodBase TargetMethod() => AccessTools.Method(
        typeof(BossGreetingCinematic).GetNestedType("BossGreetingSession", BindingFlags.NonPublic), "StartBossResponse");
    private static void Prefix() => SmokeController.Current?.GreetingEvent("boss-response");
}
