using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;

namespace NinjaSlayer.Content;

public static class NinjaSlayerTypography
{
    public const string TitleFontPath = "res://NinjaSlayer/themes/ninja_slayer_title_font.tres";
    public const string BodyFontPath = "res://NinjaSlayer/themes/ninja_slayer_body_font.tres";

    private static readonly Lazy<Font> TitleFont = new(() => GD.Load<Font>(TitleFontPath));
    private static readonly Lazy<Font> BodyFont = new(() => GD.Load<Font>(BodyFontPath));

    public static bool IsNinjaSlayerModModel(AbstractModel? model)
    {
        if (model == null)
        {
            return false;
        }

        return model switch
        {
            CardModel card => card.Pool is NinjaSlayerCardPool,
            RelicModel relic => relic.Pool is NinjaSlayerRelicPool,
            _ => model.GetType().Namespace?.StartsWith("NinjaSlayer.") == true
        };
    }

    public static bool IsNinjaSlayerHoverTip(HoverTip tip)
    {
        if (tip.Icon?.ResourcePath.StartsWith("res://NinjaSlayer/") == true)
        {
            return true;
        }

        return IsNinjaSlayerModModel(tip.CanonicalModel);
    }

    public static void ApplyTitleFont(MegaLabel label)
    {
        label.AddThemeFontOverride(ThemeConstants.Label.Font, TitleFont.Value);
    }

    public static void ApplyBodyFonts(MegaRichTextLabel label)
    {
        label.AddThemeFontOverride(ThemeConstants.RichTextLabel.NormalFont, BodyFont.Value);
        label.AddThemeFontOverride(ThemeConstants.RichTextLabel.BoldFont, BodyFont.Value);
        label.AddThemeFontOverride(ThemeConstants.RichTextLabel.ItalicsFont, BodyFont.Value);
    }
}
