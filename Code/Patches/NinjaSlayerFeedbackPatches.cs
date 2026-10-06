using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.FeedbackScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Code.Feedback;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class NinjaSlayerFeedbackOpenerPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_feedback_f2_route";
    public static string Description => "Open the independent mod feedback form for the local NinjaSlayer player's F2.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NFeedbackScreenOpener), nameof(NFeedbackScreenOpener._Input), [typeof(InputEvent)])];

    public static bool Prefix(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventKey { Pressed: true, Keycode: Key.F2 }
            || RunManager.Instance.DebugOnlyGetState() is not { } run
            || LocalContext.GetMe(run)?.Character is not INinjaSlayerCharacter)
            return true;
        if (inputEvent is InputEventKey { Echo: false })
            TaskHelper.RunSafely(NinjaSlayerFeedbackScreen.OpenAsync());
        NGame.Instance?.GetViewport().SetInputAsHandled();
        return false;
    }
}

public sealed class NinjaSlayerFirstVictoryFeedbackPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_first_victory_feedback";
    public static string Description => "Invite the local NinjaSlayer first-time winner after the native game-over entrance.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() => [new(typeof(NGameOverScreen), "AnimateIn", Type.EmptyTypes)];
    public static void Postfix(NGameOverScreen __instance, ref Task __result) =>
        __result = FirstVictoryFeedback.AfterGameOverAnimation(__result, __instance);
}
