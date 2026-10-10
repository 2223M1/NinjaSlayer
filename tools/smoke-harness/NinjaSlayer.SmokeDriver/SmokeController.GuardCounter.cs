using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private async Task RunGuardCounterAsync()
    {
        SaveManager.Instance.SetFtuesEnabled(false);
        var run = await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<NinjaSlayerCharacter>(),
            true, ActModel.GetDefaultList(), [], _configuration.Seed, GameMode.Standard, 0);
        await RunManager.Instance.EnterAct(0);
        await RunManager.Instance.EnterMapCoord(run.Map.GetAllMapPoints().First(p => p.PointType == MapPointType.Monster).coord);
        var player = LocalContext.GetMe(run)!;
        await WaitUntilAsync(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play, "Guard fixture did not start.");
        var combat = player.Creature.CombatState!;
        while (combat.HittableEnemies.Count < 3)
            await CreatureCmd.Add(combat.CreateCreature(ModelDb.Monster<ThievingHopper>().ToMutable(), CombatSide.Enemy, null));
        await WaitFrames(60);
        var targets = combat.HittableEnemies.Take(3).ToArray();
        var actor = player.Creature.GetCreatureNode()!;
        var pose = actor.Visuals.GetNode<Node2D>("%AimPose");
        var root = actor.GetGlobalTransform();
        var visual = actor.Visuals.Transform;
        var baselinePose = pose.Transform;
        var choice = new BlockingPlayerChoiceContext();
        var observer = new Harmony("NinjaSlayer.SmokeDriver.GuardCounter");
        observer.Patch(AccessTools.Method(typeof(Hook), nameof(Hook.BeforeAttack)),
            prefix: new HarmonyMethod(typeof(GuardCounterProbe), nameof(GuardCounterProbe.BeforeAttack)));
        observer.Patch(AccessTools.Method(typeof(SlowAttackAnimation), "PlaySomersault"),
            postfix: new HarmonyMethod(typeof(GuardCounterProbe), nameof(GuardCounterProbe.Animation)));
        observer.Patch(AccessTools.Method(typeof(VfxCmd), nameof(VfxCmd.PlayOnCreatures)),
            prefix: new HarmonyMethod(typeof(GuardCounterProbe), nameof(GuardCounterProbe.HitFx)));
        try
        {
            foreach (bool upgraded in new[] { false, true })
            foreach (var speed in new[] { FastModeType.Normal, FastModeType.Fast, FastModeType.Instant })
            {
                SaveManager.Instance.PrefsSave.FastMode = speed;
                foreach (var creature in targets.Append(player.Creature))
                {
                    foreach (var power in creature.Powers.ToArray()) await PowerCmd.Remove(power);
                    creature.LoseBlockInternal(creature.Block);
                    creature.SetMaxHpInternal(1000);
                    creature.SetCurrentHpInternal(1000);
                }
                foreach (var old in player.Piles.Where(p => p.Type != PileType.Deck).SelectMany(p => p.Cards).ToArray())
                    await CardPileCmd.RemoveFromCombat(old);
                for (int i = 0; i < targets.Length; i++) SetIntent(targets[i], i < 2);
                var card = combat.CreateCard<KillingIntent>(player);
                if (upgraded) card.UpgradeInternal();
                await CardPileCmd.Add(card, PileType.Hand);
                await PlayerCmd.SetEnergy(10, player);
                var action = new PlayCardAction(card, null);
                RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(action);
                await action.CompletionTask.WaitAsync(TimeSpan.FromSeconds(20));
                if (action.Exception != null) throw action.Exception;
                await Hook.AfterSideTurnEnd(combat, CombatSide.Player, [player.Creature]);
                await CreatureCmd.Damage(choice, player.Creature, 2, ValueProp.Move, targets[0]);
                await CreatureCmd.Damage(choice, player.Creature, 2, ValueProp.Move, targets[1]);
                for (int i = 0; i < targets.Length; i++) SetIntent(targets[i], i == 2);
                await PowerCmd.Apply<StrengthPower>(choice, player.Creature, 3, player.Creature, null);
                await PowerCmd.Apply<VigorPower>(choice, player.Creature, 4, player.Creature, null);
                await PowerCmd.Apply<WeakPower>(choice, player.Creature, 2, player.Creature, null);
                await PowerCmd.Apply<KaratePower>(choice, player.Creature, 3, player.Creature, null);
                foreach (var enemy in targets.Take(2)) await PowerCmd.Apply<VulnerablePower>(choice, enemy, 2, player.Creature, null);
                GuardCounterProbe.Owner = player.Creature;
                GuardCounterProbe.Commands.Clear();
                GuardCounterProbe.Animations = 0;
                GuardCounterProbe.ImpactTargets = [];
                GuardCounterProbe.PeakBeforeImpact = false;
                await Hook.AfterSideTurnEnd(combat, CombatSide.Enemy, targets);
                int damage = upgraded ? 73 : 63;
                Require(targets[0].CurrentHp == 1000 - damage && targets[1].CurrentHp == 1000 - damage
                    && targets[2].CurrentHp == 1000, "Rendered counter targets or native modifiers differ.");
                Require(GuardCounterProbe.Commands.Count == 1 && GuardCounterProbe.Commands[0].Results.Count() == 1
                    && GuardCounterProbe.Commands[0].Results.Single().Count == 2 && GuardCounterProbe.Animations == 1
                    && GuardCounterProbe.PeakBeforeImpact && GuardCounterProbe.ImpactTargets.SequenceEqual(targets.Take(2)),
                    "Counter must finish one somersault peak, then show Straight Ki hit effects on both selected targets.");
                Require(player.Creature.GetPowerAmount<KaratePower>() == 2 && !player.Creature.HasPower<VigorPower>()
                    && !player.Creature.HasPower<KillingIntentPower>() && !player.PlayerCombatState!.AllCards.OfType<StraightKi>().Any(),
                    "Rendered counter repeated resource consumption or generated a card.");
                await WaitUntilAsync(() => actor.GetGlobalTransform().IsEqualApprox(root) && actor.Visuals.Transform.IsEqualApprox(visual)
                    && pose.Transform.IsEqualApprox(baselinePose), "Counter animation did not return to idle.");
                _checkpoints.Write("guard-counter.resolved", data: new JsonObject { ["speed"] = speed.ToString(),
                    ["upgraded"] = upgraded, ["animations"] = GuardCounterProbe.Animations,
                    ["targets"] = 2, ["damagePerTarget"] = damage, ["remainingKarate"] = 2 });
            }
            _checkpoints.Write("guard-counter.completed");
        }
        finally { GuardCounterProbe.Owner = null; observer.UnpatchAll(observer.Id); }
        NGame.Instance!.Quit();
    }

    private static void SetIntent(Creature enemy, bool attack) => enemy.Monster!.SetMoveImmediate(
        new MoveState("guard-contract", _ => Task.CompletedTask, attack ? new SingleAttackIntent(2) : new BuffIntent()), true);
}

internal static class GuardCounterProbe
{
    internal static Creature? Owner;
    internal static readonly List<AttackCommand> Commands = [];
    internal static int Animations;
    internal static Creature[] ImpactTargets = [];
    internal static bool PeakBeforeImpact;
    internal static void BeforeAttack(AttackCommand __1)
    {
        if (__1.Attacker == Owner && __1.ModelSource is StraightKi) Commands.Add(__1);
    }
    internal static void Animation(Creature __0, ref Task __result)
    {
        if (__0 == Owner) __result = Complete(__result);
        static async Task Complete(Task original) { await original; Animations++; }
    }
    internal static void HitFx(IEnumerable<Creature> targets, string path)
    {
        if (Owner != null && path == VfxCmd.heavyBluntPath)
        {
            ImpactTargets = targets.ToArray();
            PeakBeforeImpact = Animations == 1;
        }
    }
}
