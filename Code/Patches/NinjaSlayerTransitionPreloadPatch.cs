using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Code.Transition;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

/// <summary>
/// Warms the transition video resource in the background as soon as the character select
/// screen is ready, so embarking does not pay the initial stream-open cost.
/// </summary>
public sealed class NinjaSlayerTransitionPreloadPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_transition_video_preload";

    public static string Description => "Preload the NinjaSlayer transition video when the character select screen opens.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen._Ready))];

    public static void Postfix()
    {
        NinjaSlayerTransitionVideo.BeginPreload();
    }
}

/// <summary>
/// Starts a hidden decoder prewarm only after the player selects NinjaSlayer.
/// </summary>
public sealed class NinjaSlayerTransitionDecoderPrewarmPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_transition_video_decoder_prewarm";

    public static string Description =>
        "Decode hidden NinjaSlayer transition frames before the first formal playback.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(NCharacterSelectScreen),
            nameof(NCharacterSelectScreen.SelectCharacter),
            [typeof(NCharacterSelectButton), typeof(CharacterModel)])
    ];

    public static void Postfix(NCharacterSelectScreen __instance, CharacterModel characterModel)
    {
        if (characterModel is INinjaSlayerCharacter)
        {
            NinjaSlayerTransitionVideoPrewarmer.TryStart(__instance);
        }
    }
}
