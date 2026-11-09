using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Powers;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class NarakuLifeHealthBarLayoutPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_naraku_life_health_bar_layout";
    public static string Description => "Display Naraku life as a flame-purple Temp HP heart at the native health bar's right edge.";
    public static bool IsCritical => false;
    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NHealthBar), "RefreshForeground")];

    public static void Postfix(NHealthBar __instance, Creature? ____creature)
    {
        int amount = ____creature?.GetPowerAmount<NarakuLifePower>() ?? 0;
        Control bar = __instance.HpBarContainer;
        Control? heart = bar.GetNodeOrNull<Control>("NinjaSlayerNarakuLife");
        NinePatchRect? outline = bar.GetNodeOrNull<NinePatchRect>("NinjaSlayerNarakuOutline");
        if (amount <= 0)
        {
            heart?.Hide();
            outline?.Hide();
            return;
        }

        if (heart == null)
        {
            // StSLib's 64px heart is centered at bar-right + (-14 + 16),
            // overlapping the bar end instead of sitting outside it.
            // Keep the host's label, scale and visibility under HpBarContainer.
            heart = (Control)__instance.GetNode<Control>("%BlockContainer").Duplicate();
            heart.Name = "NinjaSlayerNarakuLife";
            heart.Modulate = Colors.White;
            heart.MouseFilter = Control.MouseFilterEnum.Ignore;
            heart.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterRight);
            heart.OffsetLeft = -30;
            heart.OffsetRight = 34;
            heart.OffsetTop = -36;
            heart.OffsetBottom = 28;
            TextureRect icon = heart.GetNode<TextureRect>("BlockIcon");
            icon.Texture = ResourceLoader.Load<Texture2D>("res://NinjaSlayer/images/ui/naraku_temp_hp.png");
            // Match OuterColor and sparks in ninja_slayer_additive_step_fire.tscn.
            icon.Modulate = new Color(1f, 0.192157f, 1f);
            Label label = heart.GetNode<Label>("BlockLabel");
            label.AddThemeColorOverride("font_color", Colors.White);
            label.AddThemeColorOverride("font_outline_color", new Color(0.18f, 0.18f, 0.18f));
            bar.AddChild(heart);
        }
        if (outline == null)
        {
            outline = (NinePatchRect)__instance.GetNode<NinePatchRect>("%BlockOutline").Duplicate();
            outline.Name = "NinjaSlayerNarakuOutline";
            outline.Modulate = Colors.White;
            outline.SelfModulate = Colors.White;
            outline.MouseFilter = Control.MouseFilterEnum.Ignore;
            bar.AddChild(outline);
            bar.MoveChild(outline, __instance.GetNode<Control>("%BlockOutline").GetIndex() + 1);
        }
        heart.GetNode<Label>("BlockLabel").Text = amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        heart.Show();
        outline.Show();
    }
}
