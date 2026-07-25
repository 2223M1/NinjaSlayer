using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using NinjaSlayer.Code.Transition;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

/// <summary>
/// Caches the small transition resource and warms an isolated decoder/seek path from the main menu.
/// The probe renders only into an unexposed SubViewport and never gates formal playback.
/// </summary>
public sealed class NinjaSlayerTransitionPreloadPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_transition_video_preload";

    public static string Description =>
        "Cache and invisibly prime the NinjaSlayer transition video when the main menu opens.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NMainMenu), nameof(NMainMenu._Ready))];

    public static void Postfix()
    {
        NinjaSlayerTransitionVideo.BeginPreload();
        NinjaSlayerTransitionSeekPrimer.TryStart();
    }
}
