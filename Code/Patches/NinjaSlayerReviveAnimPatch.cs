using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class NinjaSlayerReviveAnimPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_revive_animation_reset";

    public static string Description => "Restore NinjaSlayer visual transforms after revival.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NCreature), nameof(NCreature.StartReviveAnim))
    ];

    public static void Postfix(NCreature __instance)
    {
        if (__instance.Entity.Player?.Character is not INinjaSlayerCharacter || __instance.HasSpineAnimation)
        {
            return;
        }

        DeathAnimation.RestoreVisual(__instance.Entity);
        __instance.SetAnimationTrigger("Idle");
    }
}
