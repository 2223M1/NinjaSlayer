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
                && !combat.Player.Creature.HasPower<KaratePower>() && combat.Player.Creature.Block == (upgraded ? 18 : 15),
                "Taunt must stack four Karate only on the chosen enemy and grant native Dexterity-modified 13/16 Block to its owner.");
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
            using var selector = UseCardSelector(new SelectCards(options =>
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
            using var selector = UseCardSelector(new SelectCards(options =>
            {
                selections++;
                Require(options.Length == (upgraded ? 5 : 4) && options.SequenceEqual(draw.Take(upgraded ? 5 : 4)),
                    "Assess must Scry four/five cards.");
                Require(combat.Player.Creature.Block == 0
                    && combat.Player.Creature.GetPowerAmount<StretchPower>() == (upgraded ? 7 : 5),
                    "Assess grants five/seven Stretch before Scry, without gaining Block or applying Dexterity to Stretch.");
                return [];
            }));
            await CardCmd.AutoPlay(Choice, card, null);
            Require(selections == 1 && !hand.ShouldRetainThisTurn && draw.All(c => c.Pile?.Type == PileType.Draw),
                "Assess's zero-choice Scry must preserve piles and not add Retain.");
            var punch = AddCard<AntiAirBangBangFist>(combat);
            Require(((MegaCrit.Sts2.Core.Localization.DynamicVars.CalculatedVar)punch.DynamicVars["CalculatedHits"])
                .Calculate(combat.Enemy) == 3, "Stretch and Dexterity each count once for Anti-Air Bang Bang Fist.");
            int hp = combat.Enemy.CurrentHp;
            await CardCmd.AutoPlay(Choice, punch, combat.Enemy);
            Require(combat.Enemy.CurrentHp == hp - 24
                && combat.Player.Creature.GetPowerAmount<StretchPower>() == (upgraded ? 7 : 5),
                "Stretch must add a real punch hit without being consumed by an attack.");
            await CardCmd.AutoPlay(Choice, hand, null);
            Require(combat.Player.Creature.Block == (upgraded ? 14 : 12)
                && combat.Player.Creature.GetPowerAmount<StretchPower>() == 0,
                "The next Defend gains its five Block plus Dexterity and Stretch, then consumes Stretch.");
        }
        GD.Print("PASS Taunt/Kunai and Assess Stretch5/7, Scry4/5, Dexterity, next-card consumption and Anti-Air buff synergy.");
    }
}
