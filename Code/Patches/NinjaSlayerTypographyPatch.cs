using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
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
        if (__instance.Model?.Pool is not NinjaSlayerCardPoolTemplate)
        {
            return;
        }

        NinjaSlayerTypography.ApplyTitleFont(__instance.GetNode<MegaLabel>("%TitleLabel"));
    }
}

public sealed class NinjaSlayerInspectRelicTypographyPatch : IPatchMethod
{
    private static readonly FieldInfo? Relics =
        AccessTools.Field(typeof(NInspectRelicScreen), "_relics");
    private static readonly FieldInfo? RelicIndex =
        AccessTools.Field(typeof(NInspectRelicScreen), "_index");

    public static string PatchId => "ninjaslayer_inspect_relic_typography";

    public static string Description => "Apply the NinjaSlayer title font to mod relic names.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NInspectRelicScreen), "UpdateRelicDisplay")];

    public static void Postfix(NInspectRelicScreen __instance)
    {
        if (Relics?.GetValue(__instance) is not IReadOnlyList<RelicModel> relics
            || RelicIndex?.GetValue(__instance) is not int index
            || index < 0
            || index >= relics.Count)
        {
            return;
        }

        RelicModel relic = relics[index];

        if (relic.Pool is not NinjaSlayerRelicPool)
        {
            return;
        }

        NinjaSlayerTypography.ApplyTitleFont(__instance.GetNode<MegaLabel>("%RelicName"));
    }
}
