using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.ExternalAnimations;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class CombatCinematicLayoutPatch : IPatchMethod
{
    private const string AdjustCreatureScaleMethod = "AdjustCreatureScaleForAspectRatio";

    public static string PatchId => "ninjaslayer_combat_cinematic_responsive_layout";

    public static string Description =>
        "Run responsive combat layout against the baseline camera during NinjaSlayer cinematics.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NCombatRoom), AdjustCreatureScaleMethod, Type.EmptyTypes)];

    public static void Prefix(NCombatRoom __instance, out bool __state)
    {
        __state = CombatCinematicCameraLease.TryBeginResponsiveLayoutAdjustment(__instance);
    }

    public static void Postfix(NCombatRoom __instance, bool __state)
    {
        if (__state)
        {
            CombatCinematicCameraLease.CompleteResponsiveLayoutAdjustment(__instance);
        }
    }
}
