using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.AutoSlay.Helpers;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Events;
using NinjaSlayer.Powers;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private async Task RunFeedback119Async()
    {
        SaveManager.Instance.SetFtuesEnabled(false);
        Func<bool> autoSlayer = NonInteractiveMode.AutoSlayerCheck;
        NonInteractiveMode.AutoSlayerCheck = static () => false;
        var probe = new Harmony("NinjaSlayer.SmokeDriver.Feedback119");
        probe.Patch(AccessTools.Method(typeof(AncientEntranceAnimation), nameof(AncientEntranceAnimation.Play), [typeof(Player)]),
            prefix: new HarmonyMethod(typeof(Release114EntranceProbe), nameof(Release114EntranceProbe.Prefix)));
        probe.Patch(AccessTools.Method(typeof(RunManager), "RollRoomTypeFor"),
            prefix: new HarmonyMethod(typeof(Feedback119RouteProbe), nameof(Feedback119RouteProbe.Prefix)));
        try
        {
            if (_configuration.Phase == SmokePhase.FeedbackFresh)
            {
                await StartFeedbackEvent(FastModeType.Normal);
                _checkpoints.Write("feedback119.fresh");
                _tree.Quit(RestartRequestedExitCode);
                return;
            }
            Release114EntranceProbe.Count = 0;
            RunState run = await ResumeBossCheckpoint();
            await RequireFeedbackEntrance(run.Players[0]);
            _checkpoints.Write("feedback119.process-reload");
            if (_configuration.Phase == SmokePhase.FeedbackResume)
            {
                await ReachFeedbackIntermission();
                _checkpoints.Write("feedback119.intermission-exit");
                _tree.Quit(RestartRequestedExitCode);
                return;
            }
            // Native checkpoints intentionally restart this embedded event at entry.
            await VerifySawatariEventCombat(CancellationToken.None, verifyFinisher: false);
            await RunManager.Instance.EnterRoomDebug(RoomType.Shop);
            await RunManager.Instance.EnterRoomDebug(RoomType.Treasure);
            _checkpoints.Write("feedback119.rewards-next-rooms");
            await NGame.Instance!.ReturnToMainMenuAfterRun();
            await WaitFrames(30);
            foreach (FastModeType speed in new[] { FastModeType.Normal, FastModeType.Fast, FastModeType.Instant })
            {
                run = await StartFeedbackEvent(speed);
                await NGame.Instance.ReturnToMainMenu();
                Release114EntranceProbe.Count = 0;
                run = await ResumeBossCheckpoint();
                await RequireFeedbackEntrance(run.Players[0]);
                await ReachFeedbackIntermission();
                await NGame.Instance.ReturnToMainMenu();
                Release114EntranceProbe.Count = 0;
                run = await ResumeBossCheckpoint();
                await RequireFeedbackEntrance(run.Players[0]);
                await VerifySawatariEventCombat(CancellationToken.None, verifyFinisher: false);
                _checkpoints.Write("feedback119.reload." + speed);
                await NGame.Instance.ReturnToMainMenuAfterRun();
                await WaitFrames(30);
            }
            _checkpoints.Write("feedback119.completed");
            _tree.Quit(0);
        }
        finally
        {
            probe.UnpatchAll(probe.Id);
            NonInteractiveMode.AutoSlayerCheck = autoSlayer;
        }
    }

    private async Task<RunState> StartFeedbackEvent(FastModeType speed)
    {
        SaveManager.Instance.PrefsSave.FastMode = speed;
        var run = await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<NinjaSlayerCharacter>(), true,
            ActModel.GetDefaultList(), [], _configuration.Seed, GameMode.Standard, 0);
        await RunManager.Instance.EnterAct(0);
        Release114EntranceProbe.Count = 0;
        // Native map travel writes the pre-room checkpoint used by Continue.
        await RunManager.Instance.EnterMapCoord(run.Map.GetAllMapPoints().First(p => p.PointType == MapPointType.Unknown).coord);
        await RunManager.Instance.FadeIn();
        await RequireFeedbackEntrance(run.Players[0]);
        return run;
    }

    private async Task RequireFeedbackEntrance(Player player)
    {
        await WaitUntilAsync(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play,
            "Sawatari reload did not begin combat.", timeout: TimeSpan.FromSeconds(30));
        Require(Release114EntranceProbe.Count == 1 && player.Creature.GetCreatureNode()!.Visuals.IsVisibleInTree(),
            "Sawatari must enter visibly exactly once after native fade-in.");
    }

    private async Task ReachFeedbackIntermission()
    {
        var state = CombatManager.Instance.DebugOnlyGetState()!;
        await CreatureCmd.Kill(state.Enemies.ToArray(), force: true);
        await CombatManager.Instance.CheckWinCondition();
        await EndSawatariPlayerTurn(CancellationToken.None);
        await WaitUntilAsync(() => CombatManager.Instance.IsPaused && GetSawatariOptions().Count == 2,
            "Sawatari did not reach its duel choice.");
    }

    private async Task VerifyFeedbackCombat()
    {
        SaveManager.Instance.SetFtuesEnabled(false);
        var choice = new BlockingPlayerChoiceContext();
        foreach (FastModeType speed in new[] { FastModeType.Normal, FastModeType.Fast, FastModeType.Instant })
        foreach (bool swallow in new[] { false, true })
        {
            SaveManager.Instance.PrefsSave.FastMode = speed;
            var run = await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<NinjaSlayerCharacter>(), true,
                ActModel.GetDefaultList(), [], _configuration.Seed, GameMode.Standard, 0);
            await RunManager.Instance.EnterAct(swallow ? 1 : 0);
            await SaveManager.Instance.SaveRun(null);
            await RunManager.Instance.EnterRoomDebug(swallow ? RoomType.Boss : RoomType.Monster,
                model: swallow ? ModelDb.Encounter<TheInsatiableBoss>().ToMutable() : ModelDb.Encounter<GremlinMercNormal>().ToMutable());
            Player player = run.Players[0];
            await WaitUntilAsync(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play, "Death fixture did not begin.");
            var enemy = CombatManager.Instance.DebugOnlyGetState()!.Enemies[0];
            if (swallow)
            {
                var liquify = (MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine.MoveState)
                    enemy.Monster!.MoveStateMachine!.States["LIQUIFY_GROUND_MOVE"];
                await liquify.PerformMove([player.Creature]);
                await PowerCmd.Remove(enemy.Powers.OfType<SandpitPower>().Single());
            }
            else await CreatureCmd.Damage(choice, player.Creature, 999, ValueProp.Move, enemy);
            await CombatManager.Instance.CheckWinCondition();
            await WaitUntilAsync(() => FindDescendant<NGameOverScreen>(_tree.Root) != null, "Death did not reach native game over.");
            _checkpoints.Write($"feedback119.death.{speed}.{(swallow ? "swallow" : "damage")}");
            await NGame.Instance.ReturnToMainMenuAfterRun();
            await WaitFrames(30);
        }
        SaveManager.Instance.PrefsSave.FastMode = FastModeType.Normal;
        foreach (bool reflect in new[] { true, false })
        {
            var run = await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<NinjaSlayerCharacter>(), true,
                ActModel.GetDefaultList(), [], _configuration.Seed, GameMode.Standard, 0);
            await RunManager.Instance.EnterAct(0);
            if (reflect) await RelicCmd.Obtain<BronzeScales>(run.Players[0]);
            await RunManager.Instance.EnterRoomDebug(reflect ? RoomType.Monster : RoomType.Boss,
                model: reflect ? ModelDb.Encounter<InkletsNormal>().ToMutable() : ModelDb.Encounter<VantomBoss>().ToMutable());
            Player player = run.Players[0];
            await WaitUntilAsync(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play, "Kill fixture did not begin.");
            var state = CombatManager.Instance.DebugOnlyGetState()!;
            foreach (var enemy in state.Enemies) await CreatureCmd.SetCurrentHp(enemy, 1);
            if (reflect)
            {
                await PowerCmd.Apply<KaratePower>(choice, player.Creature, 10, player.Creature, null);
                var button = UiHelper.FindFirst<NEndTurnButton>(MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance!)!;
                await WaitUntilAsync(() => button.IsEnabled, "Reflection End Turn unavailable.");
                await UiHelper.Click(button);
            }
            else
            {
                var strike = state.CreateCard<StrikeNinjaSlayer>(player);
                await CardPileCmd.Add(strike, PileType.Hand);
                await CardCmd.AutoPlay(choice, strike, state.Enemies[0]);
                await CombatManager.Instance.CheckWinCondition();
            }
            await WaitUntilAsync(() => FindDescendant<NRewardsScreen>(_tree.Root) != null, "Kill did not reach native rewards.",
                timeout: TimeSpan.FromSeconds(60));
            _checkpoints.Write(reflect ? "feedback119.ink-reflection" : "feedback119.vantom-kill");
            await NGame.Instance.ReturnToMainMenuAfterRun();
            await WaitFrames(30);
        }
        _checkpoints.Write("feedback119.combat-completed");
        _tree.Quit(0);
    }
}

internal static class Feedback119RouteProbe
{
    public static bool Prefix(MapPointType pointType, ref RoomType __result)
    {
        if (pointType != MapPointType.Unknown) return true;
        // Fix only encounter selection; native save, event creation and reveal still run.
        var run = RunManager.Instance.DebugOnlyGetState()!;
        Type route = typeof(TheMovingJungleEvent).Assembly.GetType("NinjaSlayer.Code.Patches.SawatariEventRoute", true)!;
        AccessTools.Method(route, "Schedule").Invoke(null, [run.Act, ModelDb.Encounter<CultistsNormal>()]);
        __result = RoomType.Event;
        return false;
    }
}
