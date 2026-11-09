using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Enchantments;

namespace NinjaSlayer.Relics;

public sealed class MaguroSushiRelic : NinjaSlayerRelicTemplate
{
    public override RelicRarity Rarity => RelicRarity.Rare;
    public override bool HasUponPickupEffect => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(3)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        HoverTipFactory.FromEnchantment<NourishingEnchantment>(1);

    public override async Task AfterObtained()
    {
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.EnchantSelectionPrompt, DynamicVars.Cards.IntValue)
        {
            Cancelable = false,
            RequireManualConfirmation = true
        };
        NourishingEnchantment enchantment = ModelDb.Enchantment<NourishingEnchantment>();
        foreach (CardModel card in await CardSelectCmd.FromDeckForEnchantment(Owner, enchantment, 1, prefs))
        {
            CardCmd.Enchant(enchantment.ToMutable(), card, 1);
            CardCmd.Preview(card);
        }
    }
}
