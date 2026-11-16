using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Events;
using NinjaSlayer.Powers;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static void GuardIntent(Creature enemy, bool attack)
    {
        if (enemy.Monster!.MoveStateMachine == null) enemy.Monster.SetUpForCombat();
        enemy.Monster.SetMoveImmediate(new MoveState("contract", _ => Task.CompletedTask,
            attack ? new SingleAttackIntent(5) : new BuffIntent()), forceTransition: true);
    }

    private static Task EndGuardSide(CombatState state, CombatSide side, params Creature[] participants) =>
#if NINJASLAYER_CHANNEL_STABLE
        Hook.AfterTurnEnd(state, side, participants);
#else
        Hook.AfterSideTurnEnd(state, side, participants);
#endif

    private static async Task VerifyV17()
    {
        MegaCrit.Sts2.Core.Context.LocalContext.NetId = 1;
        foreach (string scenario in new[] { "no attack", "intent appears", "intent disappears", "blocked",
            "partial multihit", "damage", "self", "dot", "shield", "overflow", "buffer", "heal", "reapply",
            "upgraded", "stacked", "full hand", "before play", "dead target" })
        {
            using var combat = new OrbCombat();
            var player = combat.Player.Creature;
            GuardIntent(combat.Enemy, scenario != "intent appears" && scenario != "no attack");
            if (scenario == "before play") await CreatureCmd.Damage(Choice, player, 1, ValueProp.Unpowered, player);
            var card = AddCard<KillingIntent>(combat);
            Require(card.ShouldGlowGold == (scenario != "intent appears" && scenario != "no attack"),
                "Killing Intent glow must follow current attack intent.");
            await CardCmd.AutoPlay(Choice, card, null);
            if (scenario == "intent appears") GuardIntent(combat.Enemy, true);
            if (scenario == "intent disappears") GuardIntent(combat.Enemy, false);
            if (scenario is "shield" or "overflow")
                await PowerCmd.Apply<NarakuLifePower>(Choice, player, scenario == "shield" ? 20 : 1, player, null);
            if (scenario == "buffer") await PowerCmd.Apply<BufferPower>(Choice, player, 1, player, null);
            if (scenario is not ("blocked" or "partial multihit")) player.LoseBlockInternal(player.Block);
            if (scenario == "reapply")
            {
                await CreatureCmd.Damage(Choice, player, 2, ValueProp.Move, combat.Enemy);
                await CardCmd.AutoPlay(Choice, AddCard<KillingIntent>(combat, upgraded: true), null);
            }
            if (scenario == "upgraded")
            {
                await CardCmd.AutoPlay(Choice, AddCard<KillingIntent>(combat, upgraded: true), null);
                await CardCmd.AutoPlay(Choice, AddCard<KillingIntent>(combat), null);
            }
            if (scenario == "stacked")
                await CardCmd.AutoPlay(Choice, AddCard<KillingIntent>(combat), null);
            int applications = scenario == "upgraded" ? 3 : scenario is "stacked" or "reapply" ? 2 : 1;
            Require(player.GetPowerAmount<KillingIntentPower>() == applications,
                "Repeated Killing Intent applications must stack rewards without resetting damage history.");
            var guardTip = player.GetPower<KillingIntentPower>()!.HoverTips
                .OfType<MegaCrit.Sts2.Core.HoverTips.HoverTip>().First();
            Require(!guardTip.Description.Contains('{'), "Restored power tooltip must contain no obsolete counter damage variable.");
            await EndGuardSide(combat.State, CombatSide.Player, player);
            // Next intent does not replace the condition captured before enemy actions.
            GuardIntent(combat.Enemy, false);
            if (scenario is "damage" or "self" or "dot" or "shield" or "overflow" or "buffer" or "heal" or "blocked" or "stacked")
                await CreatureCmd.Damage(Choice, player, 2,
                    scenario is "self" or "dot" ? ValueProp.Unpowered : ValueProp.Move,
                    scenario == "self" ? player : scenario == "dot" ? null! : combat.Enemy);
            if (scenario == "partial multihit")
                for (int i = 0; i < 2; i++) await CreatureCmd.Damage(Choice, player, 6, ValueProp.Move, combat.Enemy);
            if (scenario == "heal") await CreatureCmd.Heal(player, 2);
            if (scenario == "full hand") while (PileType.Hand.GetPile(combat.Player).Cards.Count < 10) combat.Card();
            if (scenario == "dead target")
            {
                combat.AddEnemy(); // Keep combat live while the snapshotted target dies.
                combat.Enemy.SetCurrentHpInternal(0);
            }
            int hp = combat.Enemy.CurrentHp;
            Require(player.HasPower<KillingIntentPower>(), "Killing Intent must remain pending during the enemy turn.");
            await EndGuardSide(combat.State, CombatSide.Enemy, combat.Enemy);
            Require(combat.Enemy.CurrentHp == hp && player.HasPower<KillingIntentPower>()
                && !combat.Player.Piles.SelectMany(p => p.Cards).OfType<StraightKi>().Any(),
                "Killing Intent must not retaliate or generate a card at enemy turn end.");
            await Hook.AfterPlayerTurnStart(combat.State, Choice, combat.Player);
            bool eligible = scenario is "intent appears" or "blocked" or "buffer" or "upgraded"
                or "full hand" or "before play" or "self" or "dot" or "stacked" or "shield" or "dead target";
            var rewards = combat.Player.Piles.SelectMany(p => p.Cards).OfType<StraightKi>().ToArray();
            Require(rewards.Length == (eligible ? applications : 0) && combat.Enemy.CurrentHp == hp,
                $"Killing Intent {scenario}: expected {(eligible ? applications : 0)} Straight Ki cards, got {rewards.Length}.");
            Require(rewards.Count(reward => reward.IsUpgraded) == (scenario == "upgraded" ? 1 : 0)
                && rewards.All(reward => reward.EnergyCost.GetWithModifiers(CostModifiers.Local) == 1 && reward.Pile?.Type ==
                (scenario == "full hand" ? PileType.Discard : PileType.Hand)),
                "Each reward must retain its source upgrade, cost 1 Energy and use native full-hand handling.");
            if (scenario == "upgraded")
                Require(rewards.Select(reward => reward.IsUpgraded).SequenceEqual(new[] { false, true, false }),
                    "Mixed rewards must preserve card play order.");
            Require(!player.HasPower<KillingIntentPower>(), $"Killing Intent {scenario} must expire at the owner's next turn start.");
            await Hook.AfterPlayerTurnStart(combat.State, Choice, combat.Player);
            Require(combat.Player.Piles.SelectMany(p => p.Cards).OfType<StraightKi>().Count() == rewards.Length,
                "Killing Intent must not grant another card on subsequent turns.");
        }

        foreach (bool stacked in new[] { false, true })
        using (var combat = new OrbCombat())
        {
            var player = combat.Player.Creature;
            var second = combat.AddEnemy();
            var buffing = combat.AddEnemy();
            GuardIntent(combat.Enemy, true);
            GuardIntent(second, true);
            GuardIntent(buffing, false);
            await CardCmd.AutoPlay(Choice, AddCard<KillingIntent>(combat), null);
            if (stacked) await CardCmd.AutoPlay(Choice, AddCard<KillingIntent>(combat, upgraded: true), null);
            await EndGuardSide(combat.State, CombatSide.Player, player);
            GuardIntent(combat.Enemy, false);
            GuardIntent(second, false);
            GuardIntent(buffing, true);
            await PowerCmd.Apply<StrengthPower>(Choice, player, 3, player, null);
            await PowerCmd.Apply<VigorPower>(Choice, player, 4, player, null);
            await PowerCmd.Apply<WeakPower>(Choice, player, 2, player, null);
            await PowerCmd.Apply<KaratePower>(Choice, player, 3, player, null);
            await PowerCmd.Apply<VulnerablePower>(Choice, combat.Enemy, 2, player, null);
            await PowerCmd.Apply<VulnerablePower>(Choice, second, 2, player, null);
            var observer = await PowerCmd.Apply<GuardAttackObserver>(Choice, player, 1, player, null);
            await EndGuardSide(combat.State, CombatSide.Enemy, combat.Enemy, second, buffing);
            await Hook.AfterPlayerTurnStart(combat.State, Choice, combat.Player);
            Require(combat.Enemy.CurrentHp == 1000 && second.CurrentHp == 1000 && buffing.CurrentHp == 1000,
                "Multiple attackers must not receive direct counter damage.");
            Require(player.GetPowerAmount<KaratePower>() == 3 && player.GetPowerAmount<VigorPower>() == 4,
                "Generating Straight Ki must not consume attack resources.");
            Require(observer!.Attacks == 0 && observer.Waves == 0 && observer.Targets == 0
                && PileType.Hand.GetPile(combat.Player).Cards.OfType<StraightKi>().Select(card => card.IsUpgraded)
                    .SequenceEqual(stacked ? new[] { false, true } : new[] { false }),
                "Mixed Killing Intent cards must grant separate base/upgraded rewards without autoplaying them.");
        }

        foreach (bool upgraded in new[] { false, true })
        {
            using var combat = new OrbCombat(ninjaSlayer: true);
            var fist = AddCard<CollapseFist>(combat, upgraded: upgraded);
            await CardCmd.AutoPlay(Choice, fist, combat.Enemy);
            Require(combat.Enemy.CurrentHp == 1000 - (upgraded ? 28 : 20)
                && combat.Player.Creature.GetPowerAmount<KaratePower>() == (upgraded ? 7 : 5),
                "Collapse Fist must double damage and retain its Karate reward.");
        }
        foreach (bool upgraded in new[] { false, true })
        {
            using var combat = new OrbCombat();
            var jujutsu = AddCard<Jujutsu>(combat, upgraded: upgraded);
            var held = combat.Card();
            await AddStock(combat.Player, 3);
            using var selector = UseCardSelector(new SelectCards(_ =>
                throw new InvalidOperationException("Jujutsu must never request a discard.")));
            int energyBefore = combat.Player.PlayerCombatState!.Energy;
            await CardCmd.AutoPlay(Choice, jujutsu, null);
            Require(held.Pile?.Type == PileType.Hand && jujutsu.Pile?.Type == PileType.Exhaust
                && combat.Stock == 3 && combat.Player.Creature.GetPowerAmount<KaratePower>() == (upgraded ? 5 : 3)
                && combat.Player.Creature.GetPowerAmount<EnergyNextTurnPower>() == 1
                && combat.Player.PlayerCombatState.Energy == energyBefore,
                "Jujutsu grants Karate3/5 and one delayed Energy, retaining Exhaust without discarding or firing Shuriken.");
            await Hook.AfterEnergyReset(combat.State, combat.Player);
            Require(combat.Player.PlayerCombatState.Energy == energyBefore + 1
                && !combat.Player.Creature.HasPower<EnergyNextTurnPower>(),
                "Jujutsu's native next-turn Energy must resolve once after Energy reset.");
            await Hook.AfterEnergyReset(combat.State, combat.Player);
            Require(combat.Player.PlayerCombatState.Energy == energyBefore + 1, "Jujutsu must not grant Energy on another turn.");
        }
        foreach (bool ninja in new[] { false, true })
        {
            using var combat = new OrbCombat(ninjaSlayer: ninja);
            var run = RunState.CreateForTest([combat.Player]);
            foreach (int act in new[] { 0, 1, 2 })
            {
                run.CurrentActIndex = act;
                Require(ModelDb.Event<YamotoKokiIsSoCuteEvent>().IsAllowed(run) == (ninja && act == 1),
                    "Yamoto Koki event must require NinjaSlayer and Act 2.");
            }
        }
        GD.Print("PASS Killing Intent restored: next-turn 1-cost Straight Ki/+, intent and damage qualification, Naraku, no retaliation, stacked ordered rewards, full hand, expiry; Collapse Fist, Jujutsu and Act-2 Koki");
    }

    public sealed class GuardAttackObserver : PowerModel
    {
        public override PowerType Type => PowerType.Buff;
        public override PowerStackType StackType => PowerStackType.Single;
        public int Attacks { get; private set; }
        public int Waves { get; private set; }
        public int Targets { get; private set; }
        public override Task AfterAttack(PlayerChoiceContext choiceContext, AttackCommand command)
        {
            if (command.Attacker == Owner)
            {
                Attacks++;
                Waves += command.Results.Count();
                Targets += command.Results.Sum(wave => wave.Count);
            }
            return Task.CompletedTask;
        }
    }
}
