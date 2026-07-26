using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using NinjaSlayer.Code.Nodes;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class TargetedRelicFlashAnchorPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_targeted_relic_flash_anchor";
    public static string Description => "Keep targeted relic flashes attached to moving creatures.";
    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(NRelicFlashVfx),
            nameof(NRelicFlashVfx.Create),
            [typeof(RelicModel), typeof(Creature)])
    ];

    public static void Postfix(Creature target, NRelicFlashVfx? __result)
    {
        if (__result != null)
        {
            NCreatureTopVfxFollower.Attach(__result, target);
        }
    }
}
