using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using NinjaSlayer.Cards.Standard;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static async Task VerifyNovember9Cards()
    {
        foreach (bool upgraded in new[] { false, true })
        foreach (int stock in new[] { 0, 1, 4 })
        {
            using var combat = new OrbCombat();
            var card = AddCard<FocusedBlows>(combat, upgraded: upgraded);
            var owner = combat.Player.Creature;
            if (stock > 0) await AddStock(combat.Player, stock);
            card.UpdateDynamicVarPreview(CardPreviewMode.Normal, combat.Enemy, card.DynamicVars);
            Require(card.DynamicVars.Damage.PreviewValue == (upgraded ? 9 : 6),
                "Focused Blows must display fixed 6/9 damage independently of Shuriken stock.");
            await PowerCmd.Apply<StrengthPower>(Choice, owner, 3, owner, null);
            await PowerCmd.Apply<VigorPower>(Choice, owner, 7, owner, null);
            await PowerCmd.Apply<WeakPower>(Choice, owner, 1, combat.Enemy, null);
            await PowerCmd.Apply<VulnerablePower>(Choice, combat.Enemy, 1, owner, null);
            card.UpdateDynamicVarPreview(CardPreviewMode.Normal, combat.Enemy, card.DynamicVars);
            decimal preview = ((upgraded ? 9 : 6) + 10) * .75m * 1.5m;
            var strike = AddCard<StrikeIronclad>(combat, upgraded: upgraded);
            strike.UpdateDynamicVarPreview(CardPreviewMode.Normal, combat.Enemy, strike.DynamicVars);
            Require(card.DynamicVars.Damage.PreviewValue == preview
                && card.DynamicVars.Damage.PreviewValue == strike.DynamicVars.Damage.PreviewValue,
                "Focused Blows must match native Strike's unrounded preview with Strength, Vigor, Weak and Vulnerable.");
            int expected = (int)preview;
            await CardCmd.AutoPlay(Choice, card, combat.Enemy);
            Require(combat.Enemy.CurrentHp == 1000 - expected && !owner.HasPower<VigorPower>()
                && !owner.HasPower<FocusPower>() && combat.Stock == stock
                && combat.Enemy.GetPowerAmount<NinjaSlayer.Powers.ShurikenAimPower>() == (upgraded ? 3 : 2),
                "Focused Blows must deal its displayed damage, apply 2/3 Aim and preserve stock.");
        }
        await VerifyShurikenAim();
        foreach (bool upgraded in new[] { false, true })
        foreach (bool handDraw in new[] { false, true })
        {
            using var combat = new OrbCombat();
            var chop = AddCard<HellChop>(combat, PileType.Draw, upgraded: upgraded);
            Require(chop.Keywords.Contains(CardKeyword.Ethereal), "Hell Chop must be Ethereal in both versions.");
            await CardPileCmd.Draw(Choice, 1, combat.Player, fromHandDraw: handDraw);
            var copies = PileType.Hand.GetPile(combat.Player).Cards.OfType<HellChop>().ToArray();
            Require(copies.Length == 2 && copies.Contains(chop) && copies.All(c => c.IsUpgraded == upgraded),
                "Drawing Hell Chop must add exactly one independent copy with the same upgrade.");
            var copy = copies.Single(c => c != chop);
            await CardCmd.AutoPlay(Choice, chop, combat.Enemy);
            Require(chop.Pile?.Type == PileType.Discard && copies[1].Owner == combat.Player
                && combat.Enemy.CurrentHp == 1000 - (upgraded ? 5 : 4),
                "Hell Chop must retain its attack and grant Karate after attacking.");
            Require(combat.Player.Creature.GetPowerAmount<NinjaSlayer.Powers.KaratePower>() == (upgraded ? 2 : 1),
                "Hell Chop must retain its original 1/2 Karate.");
            await CardPileCmd.Add(copy, PileType.Draw, CardPilePosition.Top);
            await CardPileCmd.Draw(Choice, 1, combat.Player);
            Require(PileType.Hand.GetPile(combat.Player).Cards.OfType<HellChop>().Count() == 2,
                "A later actual draw of the copy must copy again without recursively copying generated cards.");
            await CardCmd.Exhaust(Choice, copy, causedByEthereal: true);
            for (int i = 0; i < 3; i++) await CardCmd.AutoPlay(Choice, AddCard<StrikeIronclad>(combat), combat.Enemy);
            Require(chop.Pile?.Type == PileType.Discard && copy.Pile?.Type == PileType.Exhaust,
                "Three other Attacks must not return discarded or exhausted Hell Chops.");
        }
        GD.Print("PASS November 9: Focused Blows fixed damage/native preview/Aim; Hell Chop draw copies, upgrades, Ethereal and no attack-count return.");
    }
}
