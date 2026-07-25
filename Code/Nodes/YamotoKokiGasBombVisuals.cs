using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Monsters;
using STS2RitsuLib.Scaffolding.Godot;

namespace NinjaSlayer.Code.Nodes;

internal static class YamotoKokiGasBombVisuals
{
    public const string VisualsPath =
        "res://NinjaSlayer/scenes/creature_visuals/yamoto_koki_missile.tscn";

    private const float MissileScale = 0.5f;
    private const string DamageFontPath =
        "res://NinjaSlayer/themes/yamoto_koki_damage_font.tres";

    public static NCreatureVisuals Create()
    {
        NCreatureVisuals visuals =
            RitsuGodotNodeFactories.CreateFromScenePath<NCreatureVisuals>(VisualsPath)
            ?? throw new InvalidOperationException("Could not create Yamoto Koki missile visuals.");
        AddDamageAmount(visuals);
        visuals.AddChild(new NYamotoKokiGasBombIdleBob
        {
            Name = nameof(NYamotoKokiGasBombIdleBob)
        });
        visuals.SetScaleAndHue(MissileScale, 0f);
        return visuals;
    }

    private static void AddDamageAmount(NCreatureVisuals visuals)
    {
        Label damageAmount = new()
        {
            Name = "DamageAmount",
            UniqueNameInOwner = true,
            ZIndex = 20,
            OffsetLeft = 38f,
            OffsetTop = -104f,
            OffsetRight = 78f,
            OffsetBottom = -64f,
            Scale = Vector2.One * 2f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Text = YamotoKokiGasBomb.ExplodeDamage.ToString(),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        damageAmount.AddThemeColorOverride("font_color", new Color("#fff6e2"));
        damageAmount.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.25f));
        damageAmount.AddThemeColorOverride("font_outline_color", new Color(0.2f, 0.2f, 0.2f, 0.9f));
        damageAmount.AddThemeConstantOverride("shadow_offset_x", 3);
        damageAmount.AddThemeConstantOverride("shadow_offset_y", 3);
        damageAmount.AddThemeConstantOverride("outline_size", 12);
        damageAmount.AddThemeFontOverride("font", GD.Load<Font>(DamageFontPath));
        damageAmount.AddThemeFontSizeOverride("font_size", 24);

        visuals.AddChild(damageAmount);
        damageAmount.Owner = visuals;
    }
}
