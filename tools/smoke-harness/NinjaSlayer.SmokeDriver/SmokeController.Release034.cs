using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using NinjaSlayer.Content;
using NinjaSlayer.Monsters;
using System.Text.Json.Nodes;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private async Task RunRelease034Async()
    {
        var probe = new Harmony("NinjaSlayer.SmokeDriver.Release034Stages");
        var sessionType = typeof(ForestSawatariMonster).Assembly.GetType("NinjaSlayer.Code.Combat.SawatariEventSession", true)!;
        Release034StageProbe.Report = (name, ticks) => _checkpoints.Write("release034.stage." + name,
            data: new JsonObject { ["ticks"] = ticks });
        foreach (string method in new[] { "PlayNinjaSlayerEntrance", "PlaySupportTurn" })
            probe.Patch(AccessTools.Method(sessionType, method),
                prefix: new HarmonyMethod(typeof(Release034StageProbe), nameof(Release034StageProbe.Prefix)),
                postfix: new HarmonyMethod(typeof(Release034StageProbe), nameof(Release034StageProbe.Postfix)));
        SaveManager.Instance.SetFtuesEnabled(false);
        SaveManager.Instance.PrefsSave.FastMode = FastModeType.Normal;
        var run = await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<NinjaSlayerCharacter>(),
            true, ActModel.GetDefaultList(), [], _configuration.Seed, GameMode.Standard, 0);
        await RunManager.Instance.EnterAct(0);
        await RunManager.Instance.EnterMapCoord(run.Map.GetAllMapPoints().First(p => p.PointType == MapPointType.Monster).coord);
        var player = LocalContext.GetMe(run)!;
        await WaitUntilAsync(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play,
            "Release fixture did not reach a player turn.");
        string directory = Path.GetDirectoryName(_configuration.CheckpointPath)!;
        await VerifyMusicV0217Live(directory, CancellationToken.None);
        await VerifyArtifactGreetingLive();
        probe.UnpatchAll(probe.Id);
        _checkpoints.Write("release034.completed");
        _tree.Quit(0);
    }

    private async Task VerifyArtifactGreetingLive()
    {
        await NGame.Instance!.ReturnToMainMenu();
        await WaitUntilAsync(() => NGame.Instance.MainMenu != null, "Artifact fixture main menu missing.");
        var run = await NGame.Instance.StartNewSingleplayerRun(ModelDb.Character<NinjaSlayerCharacter>(),
            true, ActModel.GetDefaultList(), [], _configuration.Seed + "_ARTIFACT", GameMode.Standard, 0);
        STS2RitsuLib.Data.ModDataStore.For("NinjaSlayer")
            .Get<NinjaSlayerSettingsData>("ninja_slayer_settings").BriefBossGreetingEnabled = false;
        await RunManager.Instance.EnterAct(0);
        var probe = new Harmony("NinjaSlayer.SmokeDriver.ArtifactGreeting");
        Type greeting = typeof(NinjaSlayer.Code.ExternalAnimations.BossGreetingCinematic);
        Type session = greeting.GetNestedType("BossGreetingSession", System.Reflection.BindingFlags.NonPublic)!;
        Type bow = greeting.Assembly.GetType("NinjaSlayer.Code.ExternalAnimations.GreetingBow", true)!;
        Release034ArtifactProbe.Events.Clear();
        Release034ArtifactProbe.Bows = 0;
        Release034ArtifactProbe.VisibleDuringVoice = false;
        probe.Patch(AccessTools.Method(typeof(DarkNinjaMonster), nameof(DarkNinjaMonster.AfterAddedToRoom)),
            postfix: new HarmonyMethod(typeof(Release034ArtifactProbe), nameof(Release034ArtifactProbe.AddArtifact)));
        probe.Patch(AccessTools.Method(session, "PlaySfxWithHandle"),
            prefix: new HarmonyMethod(typeof(Release034ArtifactProbe), nameof(Release034ArtifactProbe.Sfx)));
        probe.Patch(AccessTools.GetDeclaredConstructors(bow).Single(),
            postfix: new HarmonyMethod(typeof(Release034ArtifactProbe), nameof(Release034ArtifactProbe.Bow)));
        try
        {
            await RunManager.Instance.EnterRoomDebug(MegaCrit.Sts2.Core.Rooms.RoomType.Monster,
                model: ModelDb.Encounter<NinjaSlayer.Encounters.DarkNinjaEncounter>().ToMutable());
            await WaitUntilAsync(() => LocalContext.GetMe(run)?.PlayerCombatState?.Phase == PlayerTurnPhase.Play,
                "Artifact greeting did not release combat.");
            _checkpoints.Write("release034.artifact-observed", data: new JsonObject {
                ["events"] = string.Join(",", Release034ArtifactProbe.Events),
                ["bows"] = Release034ArtifactProbe.Bows,
                ["visibleDuringVoice"] = Release034ArtifactProbe.VisibleDuringVoice,
                ["visibleAfterGreeting"] = NCombatRoom.Instance!.GetCreatureNode(LocalContext.GetMe(run)!.Creature)!.Visuals.IsVisibleInTree()
            });
            Require(Release034ArtifactProbe.Events.SequenceEqual(new[] { NinjaSlayerAudio.NinjaSlayerNoDomoEvent })
                && Release034ArtifactProbe.Bows == 0 && Release034ArtifactProbe.VisibleDuringVoice,
                "Artifact greeting must show the player, play only no-domo and skip both bows.");
            _checkpoints.Write("release034.artifact-greeting");
        }
        finally { probe.UnpatchAll(probe.Id); }
    }

    private async Task VerifyEarlySawatariPosition()
    {
        var state = CombatManager.Instance.DebugOnlyGetState()!;
        var companion = state.Creatures.Single(c => c.Monster is ForestSawatariMonster
            && c.Side == CombatSide.Player);
        var node = NCombatRoom.Instance!.GetCreatureNode(companion)!;
        var player = NCombatRoom.Instance.GetCreatureNode(LocalContext.GetMe(state)!.Creature)!;
        await WaitFrames(45);
        Vector2 arrived = node.GlobalPosition;
        Require(arrived.X > player.GlobalPosition.X + 300 && !((Sprite2D)node.Body).FlipH
            && !CombatManager.Instance.IsPaused && GetSawatariOptions().Count == 0,
            "Sawatari did not move and face the player before End Turn.");
        var layout = typeof(ForestSawatariMonster).Assembly.GetType("NinjaSlayer.Code.Patches.YamotoKokiAllyLayoutPatch", true)!;
        AccessTools.Method(layout, "Reflow").Invoke(null, [NCombatRoom.Instance]);
        await WaitFrames(20);
        Require(node.GlobalPosition.DistanceTo(arrived) < 1f, "Ally reflow pulled Sawatari back from his waiting position.");
        _checkpoints.Write("release034.early-position", data: new JsonObject { ["x"] = arrived.X, ["y"] = arrived.Y });
    }
}

internal static class Release034StageProbe
{
    internal static Action<string, ulong>? Report;
    public static void Prefix(System.Reflection.MethodBase __originalMethod) =>
        Report?.Invoke(__originalMethod.Name + ".start", Time.GetTicksMsec());
    public static async Task Postfix(Task __result, System.Reflection.MethodBase __originalMethod)
    {
        await __result;
        Report?.Invoke(__originalMethod.Name + ".end", Time.GetTicksMsec());
    }
}

internal static class Release034ArtifactProbe
{
    internal static readonly List<string> Events = [];
    internal static int Bows;
    internal static bool VisibleDuringVoice;
    public static async Task AddArtifact(Task __result, DarkNinjaMonster __instance)
    {
        await __result;
        await MegaCrit.Sts2.Core.Commands.PowerCmd.Apply<MegaCrit.Sts2.Core.Models.Powers.ArtifactPower>(
            new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext(),
            __instance.Creature, 1, __instance.Creature, null);
    }
    public static void Sfx(string eventPath)
    {
        Events.Add(eventPath);
        if (eventPath == NinjaSlayerAudio.NinjaSlayerNoDomoEvent)
        {
            VisibleDuringVoice = NCombatRoom.Instance!.GetCreatureNode(
                LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState())!.Creature)!.Visuals.IsVisibleInTree();
        }
    }
    public static void Bow() => Bows++;
}
