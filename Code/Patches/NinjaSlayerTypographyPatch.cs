using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.InspectScreens;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class NinjaSlayerInspectRelicTypographyPatch : IPatchMethod
{
    private static readonly FieldInfo Relics =
        AccessTools.Field(typeof(NInspectRelicScreen), "_relics")
        ?? throw new MissingFieldException(typeof(NInspectRelicScreen).FullName, "_relics");
    private static readonly FieldInfo RelicIndex =
        AccessTools.Field(typeof(NInspectRelicScreen), "_index")
        ?? throw new MissingFieldException(typeof(NInspectRelicScreen).FullName, "_index");

    public static string PatchId => "ninjaslayer_inspect_relic_typography";

    public static string Description => "Apply the NinjaSlayer title font to mod relic names.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NInspectRelicScreen), "UpdateRelicDisplay")];

    public static void Postfix(NInspectRelicScreen __instance)
    {
        if (Relics.GetValue(__instance) is not IReadOnlyList<RelicModel> relics)
        {
            throw new InvalidOperationException(
                "NInspectRelicScreen._relics has an unexpected runtime type.");
        }
        if (RelicIndex.GetValue(__instance) is not int index)
        {
            throw new InvalidOperationException(
                "NInspectRelicScreen._index has an unexpected runtime type.");
        }
        if (index < 0
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
