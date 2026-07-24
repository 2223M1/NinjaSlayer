using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Powers;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

[HarmonyAfter("com.ritsukage.sts2-RitsuLib.framework-core")]
[HarmonyPriority(Priority.Last)]
public sealed class NarakuLifeHealthBarLayoutPatch : IPatchMethod
{
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
            || creature == null
            || creature.GetPowerAmount<NarakuLifePower>() <= 0
            || __instance.GetParent()?.GetParent() is not NCreature creatureNode)
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
}
