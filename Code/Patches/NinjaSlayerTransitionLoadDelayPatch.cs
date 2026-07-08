using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using NinjaSlayer.Code.Transition;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class NinjaSlayerEmbarkLoadDelayPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_transition_embark_load_delay";

    public static string Description => "Delay run loading by 1.0s after the embark transition animation starts.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NCharacterSelectScreen), "StartNewSingleplayerRun", [typeof(string), typeof(List<ActModel>)])];

    public static void Prefix()
    {
        NinjaSlayerTransitionGate.LoadStartDelaySeconds = NinjaSlayerAudio.EmbarkLoadStartDelaySeconds;
    }
}

public sealed class NinjaSlayerSaveLoadDelayPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_transition_save_load_delay";

    public static string Description => "Delay run loading by 1.0s after the save-load transition animation starts.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NMainMenu), "OnContinueButtonPressedAsync")];

    public static void Prefix()
    {
        NinjaSlayerTransitionGate.LoadStartDelaySeconds = NinjaSlayerAudio.SaveLoadStartDelaySeconds;
    }
}
