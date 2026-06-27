using MegaCrit.Sts2.Core.Commands;
using NinjaSlayer.Code.Transition;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class NinjaSlayerTransitionSfxPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_character_transition_sfx_gate";

    public static string Description => "Arm NinjaSlayer frame transition when transition SFX plays.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(SfxCmd), nameof(SfxCmd.Play), [typeof(string), typeof(float)])];

    public static bool Prefix(string sfx)
    {
        if (sfx == NinjaSlayerAudio.NinjaSlayerTransitionEvent)
        {
            NinjaSlayerTransitionGate.Pending = true;
        }

        return true;
    }
}
