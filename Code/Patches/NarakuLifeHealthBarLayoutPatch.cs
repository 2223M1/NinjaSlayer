using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

[HarmonyAfter("com.ritsukage.sts2-RitsuLib.framework-core")]
[HarmonyPriority(Priority.Last)]
public sealed class NarakuLifeHealthBarLayoutPatch : IPatchMethod
{
    private const string EmbeddedStripName = "NinjaSlayerNarakuLifeStrip";
    private static readonly FieldInfo HealthBarCreature =
        AccessTools.Field(typeof(NHealthBar), "_creature")
        ?? throw new MissingFieldException(typeof(NHealthBar).FullName, "_creature");
    private static readonly FieldInfo ExpectedMaxForegroundWidth =
        AccessTools.Field(typeof(NHealthBar), "_expectedMaxFgWidth")
        ?? throw new MissingFieldException(typeof(NHealthBar).FullName, "_expectedMaxFgWidth");
    private static readonly FieldInfo OriginalBlockPosition =
        AccessTools.Field(typeof(NHealthBar), "_originalBlockPosition")
        ?? throw new MissingFieldException(typeof(NHealthBar).FullName, "_originalBlockPosition");

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
        Creature? creature = HealthBarCreature.GetValue(__instance) switch
        {
            null => null,
            Creature value => value,
            _ => throw new InvalidOperationException(
                "NHealthBar._creature has an unexpected runtime type.")
        };
        if (creature == null)
        {
            HideEmbeddedStrip(__instance);
            return;
        }

        int narakuLife = creature.GetPowerAmount<NarakuLifePower>();
        if (narakuLife <= 0)
        {
            HideEmbeddedStrip(__instance);
            return;
        }

        RefreshEmbeddedStrip(__instance, creature, narakuLife);
        NCreature creatureNode = __instance.GetParent()?.GetParent() as NCreature
            ?? throw new InvalidOperationException(
                "The active Naraku health bar is not attached to a creature node.");
        Control bounds = creatureNode.Hitbox;
        Control hpBar = __instance.HpBarContainer;
        Control block = __instance.GetNode<Control>("%BlockContainer");
        float vanillaPadding = (24f - creature.Monster?.HpBarSizeReduction).GetValueOrDefault();
        float vanillaWidth = bounds.Size.X + vanillaPadding;
        float widthMultiplier = vanillaWidth > 0f ? hpBar.Size.X / vanillaWidth : 1f;
        ExtendedHealthBarLayout layout = ExtendedHealthBarLayoutCalculator.Calculate(
            bounds.GlobalPosition.X,
            bounds.Size.X,
            vanillaPadding,
            widthMultiplier,
            block.Size.X);

        Vector2 barPosition = hpBar.GlobalPosition;
        barPosition.X = layout.BarLeft;
        hpBar.GlobalPosition = barPosition;
        Vector2 globalPosition = block.GlobalPosition;
        globalPosition.X = layout.BlockLeft;
        block.GlobalPosition = globalPosition;
        OriginalBlockPosition.SetValue(__instance, block.Position);
    }

    private static void RefreshEmbeddedStrip(NHealthBar healthBar, Creature creature, int narakuLife)
    {
        NinePatchRect poisonTemplate = healthBar.GetNode<NinePatchRect>("%PoisonForeground");
        Control mask = poisonTemplate.GetParent() as Control
            ?? throw new InvalidOperationException(
                "The poison foreground is not attached to the health-bar mask.");

        NinePatchRect? strip = mask.GetNodeOrNull<NinePatchRect>(EmbeddedStripName);
        float expectedWidth = ExpectedMaxForegroundWidth.GetValue(healthBar) is float value
            ? value
            : throw new InvalidOperationException(
                "NHealthBar._expectedMaxFgWidth has an unexpected runtime type.");
        float maxForegroundWidth = expectedWidth > 0f
            ? expectedWidth
            : healthBar.GetNode<Control>("%HpForegroundContainer").Size.X;
        EmbeddedHealthBarSegment? segment = ExtendedHealthBarLayoutCalculator.CalculateEmbeddedNarakuLife(
            creature.CurrentHp,
            creature.MaxHp,
            narakuLife,
            maxForegroundWidth,
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
        strip.OffsetLeft = segment.Value.OffsetLeft;
        strip.OffsetRight = segment.Value.OffsetRight;
        strip.Visible = true;
    }

    private static NinePatchRect CreateEmbeddedStrip(
        NHealthBar healthBar,
        Control mask,
        NinePatchRect poisonTemplate)
    {
        Control hpForeground = healthBar.GetNode<Control>("%HpForeground");

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
