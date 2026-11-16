using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;

namespace NinjaSlayer.Content;

public static class NinjaSlayerHoverTips
{
    public static IHoverTip PositiveEffects => new HoverTip(
        new LocString("static_hover_tips", "NINJA_SLAYER_POSITIVE_EFFECTS.title"),
        new LocString("static_hover_tips", "NINJA_SLAYER_POSITIVE_EFFECTS.description"));

    public static IHoverTip ChadoBreathing
    {
        get
        {
            var description = new LocString("static_hover_tips", "NINJA_SLAYER_CHADO_BREATHING.description");
            description.Add("Energy", 1);
            return new HoverTip(
                new LocString("static_hover_tips", "NINJA_SLAYER_CHADO_BREATHING.title"), description);
        }
    }
}
