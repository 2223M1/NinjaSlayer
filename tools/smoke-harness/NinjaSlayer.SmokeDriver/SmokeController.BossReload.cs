using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.AutoSlay.Helpers;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private int _bossGreetings;

    public async Task<bool> ObserveGreeting(Task<bool> task)
    {
        bool played = await task;
        if (played) _bossGreetings++;
        return played;
    }

    private async Task RunBossReloadPhaseAsync()
    {
        SaveManager.Instance.SetFtuesEnabled(enabled: false);
        RunState run;
        if (_configuration.Phase == SmokePhase.BossFresh)
        {
            run = await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<NinjaSlayerCharacter>(),
                true, ActModel.GetDefaultList(), [], _configuration.Seed, GameMode.Standard, 10);
            await RunManager.Instance.EnterAct(2);
            Player player = run.Players.Single();
            await RelicCmd.Obtain<Pantograph>(player);
            player.Creature.SetCurrentHpInternal(20);
            Require(run.Map.SecondBossMapPoint is not null, "A10 did not generate a second boss map point.");
            await RunManager.Instance.EnterMapCoord(run.Map.BossMapPoint.coord);
        }
        else
        {
            run = await ResumeBossCheckpoint();
        }
        Player owner = LocalContext.GetMe(run)!;
        await WaitUntilAsync(() => owner.PlayerCombatState?.Phase == PlayerTurnPhase.Play,
            "Boss combat did not reach the player turn.", timeout: TimeSpan.FromMinutes(2));
        var combat = CombatManager.Instance.DebugOnlyGetState()!;
        JsonObject snapshot = BossSnapshot(owner);
        if (_configuration.Phase == SmokePhase.BossFresh)
        {
            Require(owner.Creature.CurrentHp == 45, "Pantograph must heal exactly once before the first boss.");
            Require(_bossGreetings == 1, "First boss greeting did not play once.");
            _checkpoints.Write("boss.first-snapshot", data: snapshot);
        }
        else
        {
            using var reader = new StreamReader(new FileStream(_configuration.CheckpointPath, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite));
            JsonObject first = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonNode.Parse(line)!.AsObject())
                .First(row => row["Name"]?.GetValue<string>() == "boss.first-snapshot")["Data"]!.AsObject();
            Require(JsonNode.DeepEquals(first, snapshot), "Boss reload changed encounter, HP or card order.");
            Require(_bossGreetings == 0, "Reload replayed the boss greeting.");
            _checkpoints.Write("boss.reload-identical", data: snapshot);
        }
        if (_configuration.Phase != SmokePhase.BossVerify)
        {
            _tree.Quit(RestartRequestedExitCode);
            return;
        }
        await NGame.Instance!.ReturnToMainMenu();
        run = await ResumeBossCheckpoint();
        owner = LocalContext.GetMe(run)!;
        await WaitUntilAsync(() => owner.PlayerCombatState?.Phase == PlayerTurnPhase.Play,
            "Same-process boss reload did not reach the player turn.");
        Require(JsonNode.DeepEquals(snapshot, BossSnapshot(owner)) && _bossGreetings == 0,
            "Same-process reload changed combat state or replayed the greeting.");
        _checkpoints.Write("boss.same-process-identical");
        combat = CombatManager.Instance.DebugOnlyGetState()!;
        await CreatureCmd.Kill(combat.Enemies.ToArray(), force: true);
        await CombatManager.Instance.CheckWinCondition();
        await WaitUntilAsync(() => !CombatManager.Instance.IsInProgress, "First boss did not end.");
        await RunManager.Instance.EnterMapCoord(run.Map.SecondBossMapPoint!.coord);
        await WaitUntilAsync(() => owner.PlayerCombatState?.Phase == PlayerTurnPhase.Play,
            "Second boss did not reach the player turn.", timeout: TimeSpan.FromMinutes(2));
        Require(_bossGreetings == 1 && CombatManager.Instance.DebugOnlyGetState()!.Encounter!.Id.ToString() != snapshot["encounter"]!.GetValue<string>(),
            "Second boss must be a different encounter and must play its own greeting.");
        _checkpoints.Write("boss.second-greeting");
        _tree.Quit(0);
    }

    private static JsonObject BossSnapshot(Player player) => new()
    {
        ["encounter"] = CombatManager.Instance.DebugOnlyGetState()!.Encounter!.Id.ToString(),
        ["hp"] = player.Creature.CurrentHp,
        ["hand"] = new JsonArray(PileType.Hand.GetPile(player).Cards.Select(card => JsonValue.Create(card.Id.ToString())).ToArray()),
        ["draw"] = new JsonArray(PileType.Draw.GetPile(player).Cards.Select(card => JsonValue.Create(card.Id.ToString())).ToArray())
    };

    private async Task<RunState> ResumeBossCheckpoint()
    {
        await WaitUntilAsync(() => NGame.Instance?.MainMenu is not null, "Main menu did not return.");
        var button = NGame.Instance!.MainMenu!.GetNode<NButton>("MainMenuTextButtons/ContinueButton");
        await WaitUntilAsync(() => button.Visible && button.IsEnabled, "Boss checkpoint Continue button is unavailable.");
        await UiHelper.Click(button);
        await WaitUntilAsync(() => RunManager.Instance.IsInProgress && CombatManager.Instance.IsInProgress,
            "Boss checkpoint did not restore combat.", timeout: TimeSpan.FromMinutes(2));
        return RunManager.Instance.DebugOnlyGetState()!;
    }
}

[HarmonyPatch(typeof(BossGreetingCinematic), nameof(BossGreetingCinematic.TryPlay))]
internal static class BossGreetingSmokeObserver
{
    public static bool Prefix(ICombatState combatState, ref Task<bool> __result) =>
        SmokeController.Current?.TryRecordGreeting(combatState, ref __result) != true;

    public static void Postfix(ref Task<bool> __result)
    {
        if (SmokeController.Current is { } controller)
            __result = controller.ObserveGreeting(__result);
    }
}
