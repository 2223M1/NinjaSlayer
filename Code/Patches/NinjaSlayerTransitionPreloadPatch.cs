using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using NinjaSlayer.Code.Transition;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

/// <summary>
/// Warms the transition frame cache in the background as soon as the character select
/// screen is ready, so embarking plays the animation with no first-frame hitch.
/// </summary>
public sealed class NinjaSlayerTransitionPreloadPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_transition_frame_preload";

    public static string Description => "Preload NinjaSlayer transition frames when the character select screen opens.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen._Ready))];

    public static void Postfix()
    {
        NinjaSlayerTransitionFrames.BeginPreload();
    }
}
