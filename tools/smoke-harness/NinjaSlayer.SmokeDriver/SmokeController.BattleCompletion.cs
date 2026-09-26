using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Powers;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private async Task VerifyBattleCompletion(string directory, CancellationToken ct)
    {
        var manager = CombatManager.Instance;
        var player = LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState())!;
        var choice = new BlockingPlayerChoiceContext();
        var state = manager.DebugOnlyGetState()!;
        var previous = state.Enemies.ToArray();
        var giant = state.CreateCreature(ModelDb.Monster<WaterfallGiant>().ToMutable(), CombatSide.Enemy, null);
        await CreatureCmd.Add(giant);
        foreach (var enemy in previous) await CreatureCmd.Kill(enemy, force: true);
        await PowerCmd.Apply<SteamEruptionPower>(choice, giant, 20, giant, null);
        giant.SetCurrentHpInternal(1);
        foreach (var card in PileType.Hand.GetPile(player).Cards.ToArray())
            await CardPileCmd.Add(card, PileType.Discard);
        FinisherSmokeObserver.Reset();
        var chop = state.CreateCard<CommonChopRedesignV1>(player);
        await CardPileCmd.Add(chop, PileType.Hand);
        await CardCmd.AutoPlay(choice, chop, giant);
        await WaitUntilAsync(() => FinisherSmokeObserver.Snapshots().Any(s => s.CompletionObserved),
            "Giant Finisher never completed", ct);
        var finisher = FinisherSmokeObserver.Snapshots().Single();
        Require(finisher.CompletionFailure == null && finisher.ResourcesReleased,
            "Native giant death continuation failed or retained Finisher resources.");
        Require(finisher.KillAttempts.GetValueOrDefault(giant) == 1 && giant.CurrentHp == 999999999,
            "Finisher retried the giant's death or skipped its native self-destruct state.");
        Require(!await manager.CheckWinCondition() && manager.IsInProgress,
            "Giant self-destruct phase incorrectly ended combat.");
        Require(NCombatRoom.Instance!.GetCreatureNode(giant)!.GetNodeOrNull<Node>("NinjaSlayerBossDeathPresentation") == null,
            "Boss explosion started during the giant's knockout.");
        _checkpoints.Write("completion.giant-native-death-continuation");
        await PowerCmd.Remove<NarakuLifePower>(player.Creature);
        await PowerCmd.Remove<EvasionPower>(player.Creature);
        await RemoveSmokeBlock(player.Creature);
        var moves = giant.Monster!.MoveStateMachine!;
        var prepare = (MoveState)moves.States["ABOUT_TO_BLOW_MOVE"];
        giant.Monster.SetMoveImmediate(prepare, forceTransition: true);
        int hpBefore = player.Creature.CurrentHp;
        await prepare.PerformMove([player.Creature]);
        Require(player.Creature.CurrentHp == hpBefore && !giant.HasPower<SteamEruptionPower>(),
            "Native preparation turn dealt damage or retained eruption stacks.");
        var explode = (MoveState)moves.States["EXPLODE_MOVE"];
        giant.Monster.SetMoveImmediate(explode, forceTransition: true);
        Task exploding = explode.PerformMove([player.Creature]);
        await WaitUntilAsync(() => _tree.Root.FindChildren("NinjaSlayerBossBurstVideo", "", true, false)
            .OfType<Control>().Any(video => video.IsVisibleInTree()), "Giant did not show boss burst video", ct);
        await WaitUntilAsync(() => player.Creature.CurrentHp < hpBefore, "Giant burst dealt no damage", ct);
        Require(_tree.Root.FindChildren("NinjaSlayerBossBurstVideo", "", true, false)
            .OfType<Control>().Any(video => video.IsVisibleInTree()), "Native self-destruct damage missed the burst video.");
        SaveScreenshot(Path.Combine(directory, "giant-burst-damage.png"));
        await exploding;
        Require(giant.IsDead && player.Creature.CurrentHp == hpBefore - 20,
            "Giant self-destruct changed native damage or failed to die.");
        await manager.CheckWinCondition();
        _checkpoints.Write("completion.giant-burst-native-damage");

        foreach (bool delaySave in new[] { false, true })
        {
            var entry = new FightConsoleCmd().Process(player, ["TURRET_OPERATOR_WEAK"]);
            Require(entry.success, entry.msg);
            await entry.task!;
            await WaitUntilAsync(() => manager.IsInProgress && player.PlayerCombatState?.Phase == PlayerTurnPhase.Play,
                "Turret fixture did not reach player turn", ct);
            state = manager.DebugOnlyGetState()!;
            foreach (var card in PileType.Hand.GetPile(player).Cards.ToArray())
                await CardPileCmd.Add(card, PileType.Discard);
            await PowerCmd.Remove<KaratePower>(player.Creature);
            foreach (var enemy in state.Enemies) enemy.SetCurrentHpInternal(delaySave ? 1 : 7);
            for (int i = 0; i < 2; i++)
                await CardPileCmd.Add(state.CreateCard<BlackFlameRedesignV1>(player), PileType.Hand);
            int victories = 0;
            void Won(CombatRoom _) => victories++;
            manager.CombatWon += Won;
            var harmony = new Harmony("NinjaSlayer.SmokeDriver.DelayedVictorySave");
            var write = AccessTools.Method(typeof(GodotFileIo), nameof(GodotFileIo.WriteFileAsync), [typeof(string), typeof(byte[])]);
            try
            {
                if (delaySave)
                {
                    // Hold the store's completion after the real local write. This models the
                    // observed pending cloud await without contacting or changing Steam Cloud.
                    VictorySaveDelay.Reset();
                    harmony.Patch(write, postfix: new HarmonyMethod(typeof(VictorySaveDelay), nameof(VictorySaveDelay.Postfix)));
                    PlayerCmd.EndTurn(player, canBackOut: false);
                    await WaitTaskAsync(VictorySaveDelay.Written.Task, "Victory local save was not written", DefaultTimeout);
                    var saved = SaveManager.Instance.LoadRunSave();
                    Require(saved.Success && saved.SaveData!.PreFinishedRoom != null,
                        "Pending store completion did not leave a prefinished local save.");
                    Require(victories == 0 && SaveManager.Instance.CurrentRunSaveTask is { IsCompleted: false },
                        "Rewards were not gated by the pending native save.");
                    await WaitFrames(60);
                    SaveScreenshot(Path.Combine(directory, "black-flame-save-pending.png"));
                    _checkpoints.Write("completion.black-flame-save-pending-prefinished");
                    VictorySaveDelay.Release.TrySetResult();
                }
                else
                {
                    var attack = state.CreateCard<StrikeNinjaSlayerRedesignV1>(player);
                    await CardPileCmd.Add(attack, PileType.Hand);
                    await CardCmd.AutoPlay(choice, attack, state.HittableEnemies.First());
                    await manager.CheckWinCondition();
                }
                await WaitUntilAsync(() => victories == 1 && NOverlayStack.Instance?.Peek() is NRewardsScreen,
                    "Black Flame victory did not show rewards", ct);
                Require(!state.Enemies.Any(e => e.IsAlive), "Black Flame left a living enemy.");
                SaveScreenshot(Path.Combine(directory, $"black-flame-{(delaySave ? "released" : "attack")}-rewards.png"));
                _checkpoints.Write(delaySave ? "completion.black-flame-save-released-rewards" : "completion.black-flame-attack-rewards");
            }
            finally
            {
                VictorySaveDelay.Release.TrySetResult();
                harmony.Unpatch(write, HarmonyPatchType.All, harmony.Id);
                manager.CombatWon -= Won;
            }
        }
    }
}

internal static class VictorySaveDelay
{
    internal static TaskCompletionSource Written { get; private set; } = new();
    internal static TaskCompletionSource Release { get; private set; } = new();
    internal static void Reset()
    {
        Written = new();
        Release = new();
    }

    public static void Postfix(string path, ref Task __result)
    {
        if (path.EndsWith("current_run.save", StringComparison.Ordinal)) __result = Hold(__result);
    }

    private static async Task Hold(Task original)
    {
        await original;
        Written.TrySetResult();
        await Release.Task;
    }
}
