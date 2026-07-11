using Godot;
using MegaCrit.Sts2.addons.mega_text;

namespace NinjaSlayer.Content;

public static class NinjaSlayerTypography
{
    public const string TitleFontPath = "res://NinjaSlayer/themes/ninja_slayer_title_font.tres";

    private static readonly Lazy<Font> TitleFont = new(() => GD.Load<Font>(TitleFontPath));

    public static void ApplyTitleFont(MegaLabel label)
    {
        label.AddThemeFontOverride(ThemeConstants.Label.Font, TitleFont.Value);
    }
}
