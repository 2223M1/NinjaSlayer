using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Powers;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static async Task VerifyV17()
    {
        foreach (string scenario in new[] { "no attack", "intent appears", "intent disappears", "blocked", "damage", "self", "shield", "overflow", "buffer", "heal", "reapply", "upgraded", "full hand", "before play" })
        {
            using var combat = new OrbCombat();
            combat.Enemy.Monster!.SetUpForCombat();
            var player = combat.Player.Creature;
            void Intent(bool attack) => combat.Enemy.Monster!.SetMoveImmediate(new MoveState("contract", _ => Task.CompletedTask,
                attack ? new SingleAttackIntent(5) : new BuffIntent()), forceTransition: true);
            Intent(scenario != "intent appears" && scenario != "no attack");
            if (scenario == "before play") await CreatureCmd.Damage(Choice, player, 1, ValueProp.Unpowered, player);
            var card = AddCard<KillingIntentRedesignV1>(combat);
            Require(card.ShouldGlowGold == (scenario != "intent appears" && scenario != "no attack"), "Killing Intent glow must follow current attack intent.");
            await CardCmd.AutoPlay(Choice, card, null);
            if (scenario == "intent appears") Intent(true);
            if (scenario == "intent disappears") Intent(false);
            if (scenario is "shield" or "overflow") await PowerCmd.Apply<NarakuLifePower>(Choice, player, scenario == "shield" ? 20 : 1, player, null);
            if (scenario == "buffer") await PowerCmd.Apply<BufferPower>(Choice, player, 1, player, null);
            if (scenario is not "blocked") player.LoseBlockInternal(player.Block);
            if (scenario is "damage" or "self" or "shield" or "overflow" or "buffer" or "heal" or "reapply" or "blocked")
                await CreatureCmd.Damage(Choice, player, 2, scenario == "self" ? ValueProp.Unpowered : ValueProp.Move, scenario == "self" ? player : combat.Enemy);
            if (scenario == "heal") await CreatureCmd.Heal(player, 2);
            if (scenario is "reapply" or "upgraded") await CardCmd.AutoPlay(Choice, AddCard<KillingIntentRedesignV1>(combat, upgraded: true), null);
            Require(player.HasPower<KillingIntentRedesignPower>(), "Killing Intent must be applied before the end-turn snapshot.");
#if NINJASLAYER_CHANNEL_STABLE
            await Hook.AfterTurnEnd(combat.State, CombatSide.Player, [player]);
#else
            await Hook.AfterSideTurnEnd(combat.State, CombatSide.Player, [player]);
#endif
            // Enemy changing its next intent after the snapshot must not change qualification.
            Intent(false);
            if (scenario == "full hand")
                while (PileType.Hand.GetPile(combat.Player).Cards.Count < 10) combat.Card();
            await Hook.AfterPlayerTurnStart(combat.State, Choice, combat.Player);
            var generated = combat.Player.Piles.SelectMany(p => p.Cards).OfType<StraightKiRedesignV1>().ToArray();
            bool eligible = scenario is "intent appears" or "blocked" or "buffer" or "upgraded" or "full hand" or "before play" or "self";
            Require(generated.Length == (eligible ? 1 : 0), $"Killing Intent {scenario}: wrong reward count {generated.Length}.");
            if (scenario == "upgraded") Require(!generated.Single().IsUpgraded, "Both Killing Intent versions generate ordinary Straight Ki.");
            Require(!player.HasPower<KillingIntentRedesignPower>(), "Killing Intent must expire at next player turn.");
        }
        // Native damage occurring after the end-turn snapshot also invalidates the reward.
        using (var combat = new OrbCombat())
        {
            combat.Enemy.Monster!.SetUpForCombat();
            combat.Enemy.Monster!.SetMoveImmediate(new MoveState("attack", _ => Task.CompletedTask, new SingleAttackIntent(3)), true);
            await CardCmd.AutoPlay(Choice, AddCard<KillingIntentRedesignV1>(combat), null);
#if NINJASLAYER_CHANNEL_STABLE
            await Hook.AfterTurnEnd(combat.State, CombatSide.Player, [combat.Player.Creature]);
#else
            await Hook.AfterSideTurnEnd(combat.State, CombatSide.Player, [combat.Player.Creature]);
#endif
            combat.Player.Creature.LoseBlockInternal(combat.Player.Creature.Block);
            await CreatureCmd.Damage(Choice, combat.Player.Creature, 1, ValueProp.Unpowered, (MegaCrit.Sts2.Core.Entities.Creatures.Creature)null!);
            await Hook.AfterPlayerTurnStart(combat.State, Choice, combat.Player);
            Require(combat.Player.Piles.SelectMany(p => p.Cards).OfType<StraightKiRedesignV1>().Count() == 1, "Enemy-turn DOT must not invalidate Killing Intent.");
        }
        GD.Print("PASS v1.7 Killing Intent intent/glow, full block, all damage sources, Naraku absorption, reapplication, upgrade and expiry");
    }
}
