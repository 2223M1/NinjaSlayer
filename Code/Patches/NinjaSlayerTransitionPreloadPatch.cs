using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using NinjaSlayer.Code.Transition;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

/// <summary>
/// Caches the small transition resource when the main menu opens.
/// </summary>
public sealed class NinjaSlayerTransitionPreloadPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_transition_video_preload";

    public static string Description =>
        "Cache the NinjaSlayer transition video resource when the main menu opens.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NMainMenu), nameof(NMainMenu._Ready))];

    public static void Postfix() => NinjaSlayerTransitionVideo.BeginPreload();
}
