using System.Reflection;
using System.Runtime.ExceptionServices;
using Godot;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Code.Transition;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class NinjaSlayerTransitionRunPresentationPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_transition_run_presentation_root";

    public static string Description =>
        "Freeze the staged NRun node tree until the NinjaSlayer transition reveal begins.";

    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NSceneContainer), nameof(NSceneContainer.SetCurrentScene), [typeof(Control)])];

    public static void Prefix(NSceneContainer __instance, Control node)
    {
        if (NinjaSlayerPatchCapabilities.TransitionPresentationEnabled
            && node is NRun run
            && ReferenceEquals(NGame.Instance?.RootSceneContainer, __instance))
        {
            NinjaSlayerTransitionGate.TryAttachPresentationRoot(run);
        }
    }
}

public sealed class NinjaSlayerTransitionTeardownPresentationPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_transition_presentation_teardown";

    public static string Description =>
        "Cancel staged Transition presentation before run cleanup or a return to the main menu.";

    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NGame), nameof(NGame.ReturnToMainMenu), Type.EmptyTypes),
        new(typeof(NGame), nameof(NGame.ReturnToMainMenuWithInternalError), [typeof(Exception)]),
        new(typeof(NGame), nameof(NGame.GoToTimeline), Type.EmptyTypes),
        new(typeof(RunManager), nameof(RunManager.CleanUp), [typeof(bool)])
    ];

    public static void Prefix(MethodBase __originalMethod)
    {
        if (NinjaSlayerPatchCapabilities.TransitionPresentationEnabled)
        {
            NinjaSlayerTransitionGate.CancelActiveSession(
                $"Transition presentation was cancelled by {__originalMethod.DeclaringType?.Name}." +
                $"{__originalMethod.Name}.");
        }
    }
}

public sealed class NinjaSlayerTransitionCombatPresentationPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_transition_combat_presentation_barrier";

    public static string Description =>
        "Defer combat startup and its banner until the NinjaSlayer transition reveal begins.";

    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(CombatManager), nameof(CombatManager.AfterCombatRoomLoaded))];

    public static bool Prefix(CombatManager __instance) =>
        !NinjaSlayerTransitionGate.HasActiveSession
        || !NinjaSlayerPatchCapabilities.TransitionPresentationEnabled
        || !NinjaSlayerTransitionGate.TryDeferPresentation(__instance.AfterCombatRoomLoaded);
}

public sealed class NinjaSlayerTransitionAncientSetupPresentationPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_transition_ancient_setup_presentation_barrier";

    public static string Description =>
        "Defer Ancient dialogue startup until the NinjaSlayer transition reveal begins.";

    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NAncientEventLayout), nameof(NAncientEventLayout.OnSetupComplete))];

    public static bool Prefix(NAncientEventLayout __instance) =>
        !NinjaSlayerTransitionGate.HasActiveSession
        || !NinjaSlayerPatchCapabilities.TransitionPresentationEnabled
        || !NinjaSlayerTransitionGate.TryDeferPresentation(__instance.OnSetupComplete);
}

public sealed class NinjaSlayerTransitionAncientHealPresentationPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_transition_ancient_heal_presentation_barrier";

    public static string Description =>
        "Start the Ancient healing delay only after the NinjaSlayer transition reveal begins.";

    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
        GameCompatibility.TransitionPresentation.AncientHealVfx is { } target
            ? [new(target.DeclaringType!, target.Name)]
            : [];

    public static bool Prefix(
        NAncientEventLayout __instance,
        Player player,
        decimal healAmount,
        MethodBase __originalMethod,
        ref Task __result)
    {
        if (!NinjaSlayerTransitionGate.HasActiveSession
            || !NinjaSlayerPatchCapabilities.TransitionPresentationEnabled
            || !NinjaSlayerTransitionGate.TryDeferPresentation(
                () => InvokeTask(__originalMethod, __instance, [player, healAmount]),
                out Task deferred))
        {
            return true;
        }

        __result = deferred;
        return false;
    }

    private static Task InvokeTask(MethodBase method, object instance, object?[] arguments)
    {
        try
        {
            return method.Invoke(instance, arguments) as Task
                ?? throw new InvalidOperationException(
                    $"{method.DeclaringType?.FullName}.{method.Name} did not return a Task.");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }
}

public sealed class NinjaSlayerTransitionRewardSfxPresentationPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_transition_reward_sfx_presentation_barrier";

    public static string Description =>
        "Defer the loaded reward-screen cue until the NinjaSlayer transition reveal begins.";

    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(NDebugAudioManager),
            nameof(NDebugAudioManager.Play),
            [typeof(string), typeof(float), typeof(PitchVariance)])
    ];

    public static bool Prefix(
        NDebugAudioManager __instance,
        string streamName,
        float volume,
        PitchVariance variance,
        ref int __result)
    {
        if (streamName != "victory.mp3"
            || !NinjaSlayerTransitionGate.HasActiveSession
            || !NinjaSlayerPatchCapabilities.TransitionPresentationEnabled
            || !NinjaSlayerTransitionGate.TryDeferPresentation(
                () => __instance.Play(streamName, volume, variance)))
        {
            return true;
        }

        // NRewardsScreen ignores this one-shot handle.
        __result = -1;
        return false;
    }
}

public sealed class NinjaSlayerTransitionRunMusicPresentationPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_transition_run_music_presentation_barrier";

    public static string Description =>
        "Defer run music, track and ambience changes until the NinjaSlayer transition reveal begins.";

    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NRunMusicController), nameof(NRunMusicController.UpdateMusic), Type.EmptyTypes),
        new(typeof(NRunMusicController), nameof(NRunMusicController.UpdateTrack), Type.EmptyTypes),
        new(typeof(NRunMusicController), nameof(NRunMusicController.UpdateAmbience), Type.EmptyTypes)
    ];

    public static bool Prefix(NRunMusicController __instance, MethodBase __originalMethod) =>
        !NinjaSlayerTransitionGate.HasActiveSession
        || !NinjaSlayerPatchCapabilities.TransitionPresentationEnabled
        || !NinjaSlayerTransitionGate.TryDeferPresentation(
            () => __originalMethod.Invoke(__instance, null));
}

public sealed class NinjaSlayerTransitionParameterizedSfxPresentationPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_transition_parameterized_sfx_presentation_barrier";

    public static string Description =>
        "Defer parameterized room one-shot audio until the NinjaSlayer transition reveal begins.";

    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(SfxCmd),
            nameof(SfxCmd.Play),
            [typeof(string), typeof(string), typeof(float), typeof(float)])
    ];

    public static bool Prefix(MethodBase __originalMethod, object[] __args) =>
        !NinjaSlayerTransitionGate.HasActiveSession
        || !NinjaSlayerPatchCapabilities.TransitionPresentationEnabled
        || !NinjaSlayerTransitionGate.TryDeferPresentation(
            () => __originalMethod.Invoke(null, __args));
}

public sealed class NinjaSlayerTransitionLoopSfxPresentationPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_transition_loop_sfx_presentation_barrier";

    public static string Description =>
        "Defer room audio-loop lifecycle changes until the NinjaSlayer transition reveal begins.";

    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(SfxCmd), nameof(SfxCmd.PlayLoop), [typeof(string), typeof(bool)]),
        new(typeof(SfxCmd), nameof(SfxCmd.PlayLoop), [typeof(Creature), typeof(string)]),
        new(
            typeof(SfxCmd),
            nameof(SfxCmd.PlayLoop),
            [typeof(Creature), typeof(string), typeof(string), typeof(float)]),
        new(typeof(SfxCmd), nameof(SfxCmd.StopLoop), [typeof(string)]),
        new(typeof(SfxCmd), nameof(SfxCmd.StopLoop), [typeof(Creature), typeof(string)]),
        new(typeof(SfxCmd), nameof(SfxCmd.SetParam), [typeof(string), typeof(string), typeof(float)])
    ];

    public static bool Prefix(MethodBase __originalMethod, object[] __args) =>
        !NinjaSlayerTransitionGate.HasActiveSession
        || !NinjaSlayerPatchCapabilities.TransitionPresentationEnabled
        || !NinjaSlayerTransitionGate.TryDeferPresentation(
            () => __originalMethod.Invoke(null, __args));
}
