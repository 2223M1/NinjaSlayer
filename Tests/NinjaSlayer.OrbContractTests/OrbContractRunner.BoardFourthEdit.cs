using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Powers;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static async Task VerifyBoardFourthEdit()
    {
        foreach (bool upgraded in new[] { false, true })
        foreach (bool chooseSecond in new[] { false, true })
        {
            using var combat = new OrbCombat(ninjaSlayer: true);
            var other = combat.AddEnemy();
            var target = chooseSecond ? other : combat.Enemy;
            var untouched = chooseSecond ? combat.Enemy : other;
            await PowerCmd.Apply<KaratePower>(Choice, target, 3, target, null);
            await PowerCmd.Apply<KaratePower>(Choice, untouched, 5, untouched, null);
            await PowerCmd.Apply<DexterityPower>(Choice, combat.Player.Creature, 2, combat.Player.Creature, null);
            var card = AddCard<NinjaTaunt>(combat, upgraded: upgraded);
            Require(card.TargetType == TargetType.AnyEnemy, "Taunt must expose native selected-enemy targeting.");
            await CardCmd.AutoPlay(Choice, card, target);
            Require(target.GetPowerAmount<KaratePower>() == 7 && untouched.GetPowerAmount<KaratePower>() == 5
                && !combat.Player.Creature.HasPower<KaratePower>() && combat.Player.Creature.Block == (upgraded ? 19 : 16),
                "Taunt must stack four Karate only on the chosen enemy and grant native Dexterity-modified 14/17 Block to its owner.");
        }
        foreach (bool upgraded in new[] { false, true })
        foreach (bool discardScry in new[] { false, true })
        {
            using var combat = new OrbCombat();
            var card = AddCard<KunaiThrow>(combat, upgraded: upgraded);
            var handCard = AddCard<DefendIronclad>(combat);
            var retainedHand = AddCard<StrikeIronclad>(combat);
            var draw = Enumerable.Range(0, 6).Select(_ => AddCard<DefendIronclad>(combat, PileType.Draw)).ToArray();
            int seen = upgraded ? 4 : 3, selections = 0;
            using var selector = CardSelectCmd.UseSelector(new SelectCards(options =>
            {
                if (selections++ == 0)
                {
                    Require(options.Length == seen && options.SequenceEqual(draw.Take(seen)),
                        "Kunai must Scry three/four actual top cards before its hand discard.");
                    return discardScry ? [options[^1]] : [];
                }
                Require(options.Length == 2 && options.Contains(handCard) && options.Contains(retainedHand),
                    "Kunai's final discard must select from Hand, not its Scry snapshot.");
                return [handCard];
            }));
            await CardCmd.AutoPlay(Choice, card, combat.Enemy);
            Require(selections == 2 && combat.Enemy.CurrentHp == 1000 - (upgraded ? 11 : 9)
                && handCard.Pile?.Type == PileType.Discard && retainedHand.Pile?.Type == PileType.Hand
                && draw.Count(c => c.Pile?.Type == PileType.Discard) == (discardScry ? 1 : 0),
                "Kunai must retain unchanged damage and one hand discard after both zero-choice and selected Scry.");
        }
        foreach (bool upgraded in new[] { false, true })
        {
            using var combat = new OrbCombat();
            var card = AddCard<Assess>(combat, upgraded: upgraded);
            var hand = AddCard<DefendIronclad>(combat);
            var draw = Enumerable.Range(0, 7).Select(_ => AddCard<DefendIronclad>(combat, PileType.Draw)).ToArray();
            await PowerCmd.Apply<DexterityPower>(Choice, combat.Player.Creature, 2, combat.Player.Creature, null);
            int selections = 0;
            using var selector = CardSelectCmd.UseSelector(new SelectCards(options =>
            {
                selections++;
                Require(options.Length == (upgraded ? 6 : 4) && options.SequenceEqual(draw.Take(upgraded ? 6 : 4)),
                    "Assess must keep its four/six-card Scry.");
                Require(combat.Player.Creature.Block == (upgraded ? 8 : 6), "Assess must gain four/six native Block before Scry.");
                return [];
            }));
            await CardCmd.AutoPlay(Choice, card, null);
            Require(selections == 1 && !hand.ShouldRetainThisTurn && draw.All(c => c.Pile?.Type == PileType.Draw),
                "Assess's zero-choice Scry must preserve piles and not add Retain.");
        }
        GD.Print("PASS fourth v1.19 edit: selected-enemy Taunt base+upgrade/stacking, Kunai Scry3/4 before hand discard, Assess Block4/6 plus Scry4/6 and native Dexterity.");
    }
}
