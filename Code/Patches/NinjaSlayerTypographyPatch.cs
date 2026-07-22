using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens.InspectScreens;
using NinjaSlayer.Code.Compatibility;
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

public sealed class NinjaSlayerInspectRelicTypographyPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_inspect_relic_typography";

    public static string Description => "Apply the NinjaSlayer title font to mod relic names.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NInspectRelicScreen), "UpdateRelicDisplay")];

    public static void Postfix(NInspectRelicScreen __instance)
    {
        if (!GameCompatibility.Typography.TryGetSelectedRelic(__instance, out RelicModel? relic)
            || relic is null)
        {
            return;
        }

        if (relic.Pool is not NinjaSlayerRelicPool)
        {
            return;
        }

        NinjaSlayerTypography.ApplyTitleFont(__instance.GetNode<MegaLabel>("%RelicName"));
    }
}
