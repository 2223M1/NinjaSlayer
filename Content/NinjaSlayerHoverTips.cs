using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;

namespace NinjaSlayer.Content;

public static class NinjaSlayerHoverTips
{
    public static IHoverTip ChadoBreathing => new HoverTip(
        new LocString("static_hover_tips", "NINJA_SLAYER_CHADO_BREATHING.title"),
        new LocString("static_hover_tips", "NINJA_SLAYER_CHADO_BREATHING.description"));

}
