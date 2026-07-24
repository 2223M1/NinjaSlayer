using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Code.Transition;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

/// <summary>
/// Warms the transition resource and official player as soon as the main menu is ready.
/// This covers both new runs and continuing a saved run.
/// </summary>
public sealed class NinjaSlayerTransitionPreloadPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_transition_video_preload";

    public static string Description =>
        "Preload and decode the NinjaSlayer transition video when the main menu opens.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NMainMenu), nameof(NMainMenu._Ready))];

    public static void Postfix()
    {
        NinjaSlayerTransitionVideo.BeginPreload();
        NinjaSlayerTransitionVideoPrewarmer.TryStart();
    }
}

/// <summary>
/// Retries the hidden decoder prewarm after NinjaSlayer is selected if the main-menu attempt failed.
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

    public static void Postfix(CharacterModel characterModel)
    {
        if (characterModel is INinjaSlayerCharacter)
        {
            NinjaSlayerTransitionVideoPrewarmer.TryStart();
        }
    }
}
