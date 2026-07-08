using System.Collections.Generic;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.InspectScreens;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class NinjaSlayerCardTitleTypographyPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_card_title_typography";

    public static string Description => "Apply Farrier title font to NinjaSlayer mod cards.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NCard), "UpdateTitleLabel")];

    public static void Postfix(NCard __instance)
    {
        if (__instance.Model?.Pool is not NinjaSlayerCardPool)
        {
            return;
        }

        NinjaSlayerTypography.ApplyTitleFont(__instance.GetNode<MegaLabel>("%TitleLabel"));
    }
}

public sealed class NinjaSlayerCardBodyTypographyPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_card_body_typography";

    public static string Description => "Apply Farrier body font to NinjaSlayer mod card descriptions.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NCard), nameof(NCard.UpdateVisuals), [typeof(PileType), typeof(CardPreviewMode)])];

    public static void Postfix(NCard __instance)
    {
        if (__instance.Model?.Pool is not NinjaSlayerCardPool)
        {
            return;
        }

        NinjaSlayerTypography.ApplyBodyFonts(__instance.GetNode<MegaRichTextLabel>("%DescriptionLabel"));
    }
}

public sealed class NinjaSlayerHoverTipTypographyPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_hover_tip_typography";

    public static string Description => "Apply Farrier fonts to NinjaSlayer mod hover tips.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NHoverTipSet), "Init", [typeof(Control), typeof(IEnumerable<IHoverTip>)])];

    public static void Postfix(NHoverTipSet __instance, IEnumerable<IHoverTip> hoverTips)
    {
        VFlowContainer container = __instance.GetNode<VFlowContainer>("textHoverTipContainer");
        int hoverTipControlIndex = 0;

        foreach (IHoverTip item in IHoverTip.RemoveDupes(hoverTips))
        {
            if (item is not HoverTip hoverTip)
            {
                continue;
            }

            if (NinjaSlayerTypography.IsNinjaSlayerHoverTip(hoverTip))
            {
                var control = (Control)container.GetChild(hoverTipControlIndex);
                NinjaSlayerTypography.ApplyTitleFont(control.GetNode<MegaLabel>("%Title"));
                NinjaSlayerTypography.ApplyBodyFonts(control.GetNode<MegaRichTextLabel>("%Description"));
            }

            hoverTipControlIndex++;
        }
    }
}

public sealed class NinjaSlayerInspectRelicTypographyPatch : IPatchMethod
{
    private static readonly FieldInfo? RelicsField = AccessTools.Field(typeof(NInspectRelicScreen), "_relics");
    private static readonly FieldInfo? IndexField = AccessTools.Field(typeof(NInspectRelicScreen), "_index");

    public static string PatchId => "ninjaslayer_inspect_relic_typography";

    public static string Description => "Apply Farrier fonts to NinjaSlayer mod relic inspect screen text.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NInspectRelicScreen), "UpdateRelicDisplay")];

    public static void Postfix(NInspectRelicScreen __instance)
    {
        if (RelicsField?.GetValue(__instance) is not IReadOnlyList<RelicModel> relics ||
            IndexField?.GetValue(__instance) is not int index ||
            index < 0 ||
            index >= relics.Count)
        {
            return;
        }

        if (relics[index].Pool is not NinjaSlayerRelicPool)
        {
            return;
        }

        NinjaSlayerTypography.ApplyTitleFont(__instance.GetNode<MegaLabel>("%RelicName"));
        NinjaSlayerTypography.ApplyBodyFonts(__instance.GetNode<MegaRichTextLabel>("%RelicDescription"));
    }
}
