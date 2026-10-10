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
            "upgraded", "full hand", "before play", "dead target" })
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
            await EndGuardSide(combat.State, CombatSide.Player, player);
            // Next intent does not replace the targets captured before the enemy actions.
            GuardIntent(combat.Enemy, false);
            if (scenario is "damage" or "self" or "dot" or "shield" or "overflow" or "buffer" or "heal" or "blocked")
                await CreatureCmd.Damage(Choice, player, 2,
                    scenario is "self" or "dot" ? ValueProp.Unpowered : ValueProp.Move,
                    scenario == "self" ? player : scenario == "dot" ? null! : combat.Enemy);
            if (scenario == "partial multihit")
                for (int i = 0; i < 2; i++) await CreatureCmd.Damage(Choice, player, 5, ValueProp.Move, combat.Enemy);
            if (scenario == "heal") await CreatureCmd.Heal(player, 2);
            if (scenario == "full hand") while (PileType.Hand.GetPile(combat.Player).Cards.Count < 10) combat.Card();
            if (scenario == "dead target")
            {
                combat.AddEnemy(); // Keep combat live while the snapshotted target dies.
                combat.Enemy.SetCurrentHpInternal(0);
            }
            int hp = combat.Enemy.CurrentHp;
            Require(player.HasPower<KillingIntentPower>(), "Counter must wait until the enemy turn ends.");
            await EndGuardSide(combat.State, CombatSide.Enemy, combat.Enemy);
            bool eligible = scenario is "intent appears" or "blocked" or "buffer" or "upgraded"
                or "full hand" or "before play" or "self" or "dot";
            int expected = eligible ? scenario == "upgraded" ? 56 : 47 : 0;
            Require(hp - combat.Enemy.CurrentHp == expected,
                $"Killing Intent {scenario}: expected {expected}, got {hp - combat.Enemy.CurrentHp} damage.");
            Require(!player.HasPower<KillingIntentPower>(), $"Counter {scenario} must expire at enemy turn end.");
            await EndGuardSide(combat.State, CombatSide.Enemy, combat.Enemy);
            Require(hp - combat.Enemy.CurrentHp == expected, "Counter must not repeat on later turn hooks.");
            Require(!combat.Player.Piles.SelectMany(p => p.Cards).OfType<StraightKi>().Any(),
                "Counter must not generate or play a Straight Ki card, including with a full hand.");
        }

        using (var combat = new OrbCombat())
        {
            var player = combat.Player.Creature;
            var second = combat.AddEnemy();
            var buffing = combat.AddEnemy();
            GuardIntent(combat.Enemy, true);
            GuardIntent(second, true);
            GuardIntent(buffing, false);
            await CardCmd.AutoPlay(Choice, AddCard<KillingIntent>(combat), null);
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
            // floor((47 + 3 Strength + 4 Vigor) * .75 Weak * 1.5 Vulnerable) + 3 Karate.
            Require(combat.Enemy.CurrentHp == 937 && second.CurrentHp == 937 && buffing.CurrentHp == 1000,
                "One counter must apply native modifiers to both snapshotted attackers, excluding the new attack intent.");
            Require(player.GetPowerAmount<KaratePower>() == 2 && !player.HasPower<VigorPower>(),
                "One AoE counter must consume Karate and Vigor once for the entire batch.");
            Require(observer!.Attacks == 1 && observer.Waves == 1 && observer.Targets == 2,
                "Counter must publish one native attack with one two-target result wave.");
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
            using var selector = CardSelectCmd.UseSelector(new SelectCards(_ =>
                throw new InvalidOperationException("Jujutsu must never request a discard.")));
            await CardCmd.AutoPlay(Choice, jujutsu, null);
            Require(held.Pile?.Type == PileType.Hand && jujutsu.Pile?.Type == PileType.Exhaust
                && combat.Stock == 3 && combat.Player.Creature.GetPowerAmount<KaratePower>() == (upgraded ? 5 : 3),
                "Jujutsu must retain its cost, Karate and Exhaust without discarding or firing Shuriken.");
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
        GD.Print("PASS guard counter: enemy-turn timing, one native AoE wave, intent snapshot, full block, Naraku, modifiers, repeated plays, expiry; Collapse Fist, Jujutsu and Act-2 Koki");
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
