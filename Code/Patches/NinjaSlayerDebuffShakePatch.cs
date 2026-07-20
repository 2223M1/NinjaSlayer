using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class NinjaSlayerDebuffShakePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_debuff_shake";
    public static string Description => "Match the vanilla player debuff shake on NinjaSlayer visuals.";
    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NCreature), nameof(NCreature.AnimShake))];

    public static bool Prefix(NCreature __instance)
    {
        if (__instance.Entity.Player?.Character is not INinjaSlayerCharacter)
        {
            return true;
        }

        NinjaSlayerDebuffShakeAnimation.Play(__instance);
        return false;
    }
}
