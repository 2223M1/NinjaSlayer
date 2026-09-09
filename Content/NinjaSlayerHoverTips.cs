using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Cards.RedesignV1;

namespace NinjaSlayer.Content;

public static class NinjaSlayerHoverTips
{
    public static IHoverTip ChadoBreathing => new HoverTip(
        new LocString("static_hover_tips", "NINJA_SLAYER_CHADO_BREATHING.title"),
        new LocString("static_hover_tips", "NINJA_SLAYER_CHADO_BREATHING.description"));

    public static IEnumerable<IHoverTip> ExhaustingChop(bool upgraded)
    {
        var card = ModelDb.Card<CommonChopRedesignV1>().ToMutable();
        if (upgraded) card.UpgradeInternal();
        card.AddKeyword(CardKeyword.Exhaust);
        return [HoverTipFactory.FromCard(card), .. card.HoverTips];
    }
}
