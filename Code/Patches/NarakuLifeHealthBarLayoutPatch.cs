using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

[HarmonyAfter("com.ritsukage.sts2-RitsuLib.framework-core")]
[HarmonyPriority(Priority.Last)]
public sealed class NarakuLifeHealthBarLayoutPatch : IPatchMethod
{
    private const string EmbeddedStripName = "NinjaSlayerNarakuLifeStrip";

    public static string PatchId => "ninjaslayer_naraku_life_health_bar_layout";

    public static string Description =>
        "Keep the vanilla block anchor fixed while Naraku life extends the health bar to the right.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NHealthBar), "RefreshForeground"),
        new(typeof(NHealthBar), "SetHpBarContainerSizeWithOffsetsImmediately", true)
    ];

    public static void Postfix(NHealthBar __instance)
    {
        if (!GameCompatibility.NarakuHealthBar.TryGetCreature(__instance, out Creature? creature)
            || creature == null)
        {
            HideEmbeddedStrip(__instance);
            return;
        }

        int narakuLife = creature.GetPowerAmount<NarakuLifePower>();
        RefreshEmbeddedStrip(__instance, creature, narakuLife);
        if (narakuLife <= 0 || __instance.GetParent()?.GetParent() is not NCreature creatureNode)
        {
            return;
        }

        Control bounds = creatureNode.Hitbox;
        Control hpBar = __instance.HpBarContainer;
        float vanillaPadding = (24f - creature.Monster?.HpBarSizeReduction).GetValueOrDefault();
        float vanillaWidth = bounds.Size.X + vanillaPadding;
        float widthMultiplier = vanillaWidth > 0f ? hpBar.Size.X / vanillaWidth : 1f;
        ExtendedHealthBarLayout layout = ExtendedHealthBarLayoutCalculator.Calculate(
            bounds.GlobalPosition.X,
            bounds.Size.X,
            vanillaPadding,
            widthMultiplier,
            __instance.GetNodeOrNull<Control>("%BlockContainer")?.Size.X ?? 0f);

        Vector2 barPosition = hpBar.GlobalPosition;
        barPosition.X = layout.BarLeft;
        hpBar.GlobalPosition = barPosition;
        GameCompatibility.NarakuHealthBar.AnchorBlock(__instance, layout.BlockLeft);
    }

    private static void RefreshEmbeddedStrip(NHealthBar healthBar, Creature creature, int narakuLife)
    {
        NinePatchRect? poisonTemplate = healthBar.GetNodeOrNull<NinePatchRect>("%PoisonForeground");
        if (poisonTemplate?.GetParent() is not Control mask)
        {
            return;
        }

        NinePatchRect? strip = mask.GetNodeOrNull<NinePatchRect>(EmbeddedStripName);
        EmbeddedHealthBarSegment? segment = ExtendedHealthBarLayoutCalculator.CalculateEmbeddedNarakuLife(
            creature.CurrentHp,
            creature.MaxHp,
            narakuLife,
            GameCompatibility.NarakuHealthBar.GetMaxForegroundWidth(healthBar),
            poisonTemplate.PatchMarginLeft);
        if (segment == null)
        {
            if (strip != null)
            {
                strip.Visible = false;
            }

            return;
        }

        strip ??= CreateEmbeddedStrip(healthBar, mask, poisonTemplate);
        if (strip == null)
        {
            return;
        }

        strip.OffsetLeft = segment.Value.OffsetLeft;
        strip.OffsetRight = segment.Value.OffsetRight;
        strip.Visible = true;
    }

    private static NinePatchRect? CreateEmbeddedStrip(
        NHealthBar healthBar,
        Control mask,
        NinePatchRect poisonTemplate)
    {
        Control? hpForeground = healthBar.GetNodeOrNull<Control>("%HpForeground");
        if (hpForeground == null)
        {
            return null;
        }

        NinePatchRect strip = (NinePatchRect)poisonTemplate.Duplicate();
        strip.Name = EmbeddedStripName;
        strip.Visible = false;
        strip.Modulate = Colors.White;
        strip.SelfModulate = NarakuLifeHealthBarColors.Foreground;
        strip.Material = null;
        strip.ZIndex = 0;
        strip.MouseFilter = Control.MouseFilterEnum.Ignore;
        mask.AddChild(strip);
        mask.MoveChild(strip, Math.Clamp(hpForeground.GetIndex() + 1, 0, mask.GetChildCount() - 1));
        return strip;
    }

    private static void HideEmbeddedStrip(NHealthBar healthBar)
    {
        healthBar
            .GetNodeOrNull<NinePatchRect>("%PoisonForeground")
            ?.GetParent()
            ?.GetNodeOrNull<NinePatchRect>(EmbeddedStripName)
            ?.Hide();
    }
}
