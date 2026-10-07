using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using NinjaSlayer.Cards.Standard;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static async Task VerifyStrikeSynergy()
    {
        var expected = new[] { ModelDb.Card<StrikeNinjaSlayer>().Id, ModelDb.Card<ChopStrike>().Id, ModelDb.Card<StrikeStrike>().Id };
        var actual = ModelDb.CardPool<NinjaSlayer.Content.NinjaSlayerCardPool>().AllCards
            .Where(card => card.Tags.Contains(CardTag.Strike)).Select(card => card.Id).ToHashSet();
        Require(actual.SetEquals(expected), "Exactly the three NinjaSlayer Strike cards must use the native Strike tag.");
        foreach (bool upgraded in new[] { false, true })
        foreach (int relics in new[] { 0, 1, 2, 3 })
        {
            using var combat = new OrbCombat(ninjaSlayer: true);
            if ((relics & 1) != 0) combat.Player.AddRelicInternal(ModelDb.Relic<StrikeDummy>().ToMutable());
            if ((relics & 2) != 0) combat.Player.AddRelicInternal(ModelDb.Relic<FakeStrikeDummy>().ToMutable());
            int extra = ((relics & 1) != 0 ? 3 : 0) + ((relics & 2) != 0 ? 1 : 0);
            CardModel[] strikes = [AddCard<StrikeNinjaSlayer>(combat, upgraded: upgraded),
                AddCard<ChopStrike>(combat, upgraded: upgraded), AddCard<StrikeStrike>(combat, upgraded: upgraded)];
            using var selection = CardSelectCmd.UseSelector(new SelectCards(_ => []));
            foreach (CardModel card in strikes)
            {
                Require(card.Tags.Contains(CardTag.Strike)
                    && card.IsBasicStrikeOrDefend == (card is StrikeNinjaSlayer),
                    "Strike classification must retain basic-card rarity boundaries.");
                decimal before = combat.Enemy.CurrentHp;
                await CardCmd.AutoPlay(Choice, card, combat.Enemy);
                Require(before - combat.Enemy.CurrentHp == card.DynamicVars.Damage.BaseValue + extra,
                    $"{card.Id} real HP damage did not include its native Strike relic bonus.");
            }
            var generated = combat.Player.PlayerCombatState!.AllCards.OfType<StrikeStrike>()
                .Single(card => !ReferenceEquals(card, strikes[2]));
            Require(generated.Tags.Contains(CardTag.Strike) && generated.IsUpgraded == upgraded,
                "Fresh generated Strike cards must retain Strike classification and upgrade state.");
            decimal generatedBefore = combat.Enemy.CurrentHp;
            await CardCmd.AutoPlay(Choice, generated, combat.Enemy);
            Require(generatedBefore - combat.Enemy.CurrentHp == generated.DynamicVars.Damage.BaseValue + extra,
                "A freshly generated Strike must receive the real relic damage bonus.");
            var nonStrike = AddCard<Chop>(combat, upgraded: upgraded);
            decimal nonStrikeBefore = combat.Enemy.CurrentHp;
            await CardCmd.AutoPlay(Choice, nonStrike, combat.Enemy);
            Require(!nonStrike.Tags.Contains(CardTag.Strike)
                && nonStrikeBefore - combat.Enemy.CurrentHp == nonStrike.DynamicVars.Damage.BaseValue,
                "Ordinary non-Strike attacks must not gain Strike relic damage.");
        }
        foreach (bool upgraded in new[] { false, true })
        {
            using var combat = new OrbCombat(ninjaSlayer: true);
            var perfected = AddCard<PerfectedStrike>(combat, upgraded: upgraded);
            CardModel[] strikes = [AddCard<StrikeNinjaSlayer>(combat), AddCard<ChopStrike>(combat), AddCard<StrikeStrike>(combat)];
            await CardPileCmd.Add(strikes[0], PileType.Draw);
            await CardPileCmd.Add(strikes[1], PileType.Discard);
            await CardPileCmd.Add(strikes[2], PileType.Exhaust);
            AddCard<Chop>(combat);
            // The persistent deck is not a combat pile and must not be counted again.
            AddCard<StrikeNinjaSlayer>(combat, PileType.Deck);
            int count = 4; // Perfected Strike itself plus the three owned NinjaSlayer Strikes.
            decimal expectedDamage = 6 + (upgraded ? 3 : 2) * count;
            Require(perfected.DynamicVars.CalculatedDamage.Calculate(combat.Enemy) == expectedDamage,
                "Native Perfected Strike preview must count draw/discard/exhaust Strikes exactly once.");
            decimal before = combat.Enemy.CurrentHp;
            await CardCmd.AutoPlay(Choice, perfected, combat.Enemy);
            Require(before - combat.Enemy.CurrentHp == expectedDamage,
                "Native Perfected Strike real damage must use the same combat Strike count.");
        }
        GD.Print("PASS native Strike tags, base/upgraded and generated Strikes, both Dummy relics, non-Strike controls and Perfected Strike preview/real damage");
    }
}
