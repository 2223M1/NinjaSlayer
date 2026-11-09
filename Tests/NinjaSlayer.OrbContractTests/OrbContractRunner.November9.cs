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
            var ash = AddCard<AshenStrike>(combat, upgraded: upgraded);
            var owner = combat.Player.Creature;
            if (stock > 0) await AddStock(combat.Player, stock);
            for (int i = 0; i < stock; i++) AddCard<Wound>(combat, PileType.Exhaust);
            card.UpdateDynamicVarPreview(CardPreviewMode.Normal, combat.Enemy, card.DynamicVars);
            Require(card.DynamicVars.CalculatedDamage.PreviewValue == 6 + stock * (upgraded ? 4 : 3),
                "Focused Blows must display 6 plus the current Shuriken stock times 3/4.");
            await PowerCmd.Apply<StrengthPower>(Choice, owner, 3, owner, null);
            await PowerCmd.Apply<VigorPower>(Choice, owner, 7, owner, null);
            await PowerCmd.Apply<WeakPower>(Choice, owner, 1, combat.Enemy, null);
            await PowerCmd.Apply<VulnerablePower>(Choice, combat.Enemy, 1, owner, null);
            card.UpdateDynamicVarPreview(CardPreviewMode.Normal, combat.Enemy, card.DynamicVars);
            ash.UpdateDynamicVarPreview(CardPreviewMode.Normal, combat.Enemy, ash.DynamicVars);
            int expected = (int)card.DynamicVars.CalculatedDamage.PreviewValue;
            Require(card.DynamicVars.CalculatedDamage.PreviewValue == ash.DynamicVars.CalculatedDamage.PreviewValue,
                "Focused Blows preview must match native Ashen Strike with equivalent stacks and damage modifiers.");
            string description = System.Text.RegularExpressions.Regex.Replace(
                card.GetDescriptionForPile(PileType.Hand, combat.Enemy), @"\[[^\]]*\]", "");
            Require(description.Contains(expected.ToString()), "Focused Blows must show the calculated damage in its description.");
            await CardCmd.AutoPlay(Choice, card, combat.Enemy);
            Require(combat.Enemy.CurrentHp == 1000 - expected && !owner.HasPower<VigorPower>()
                && !owner.HasPower<FocusPower>() && combat.Stock == stock,
                "Focused Blows actual damage must match its preview without spending stock or granting Focus.");
            await AddStock(combat.Player, 2);
            card.UpdateDynamicVarPreview(CardPreviewMode.Normal, combat.Enemy, card.DynamicVars);
            Require(card.DynamicVars.CalculatedDamage.Calculate(combat.Enemy) == 6 + (stock + 2) * (upgraded ? 4 : 3),
                "Focused Blows must update from live stock instead of retaining its old preview.");
        }

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
        GD.Print("PASS November 9: Focused Blows live stock/Ashen Strike preview/actual damage; Hell Chop draw copies, upgrades, Ethereal and no attack-count return.");
    }
}
