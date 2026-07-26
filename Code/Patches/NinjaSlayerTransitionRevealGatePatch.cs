using System;
using System.Threading;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using NinjaSlayer.Code.Transition;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

/// <summary>
/// Embark reveal path (<c>RunManager.FadeIn</c> -> <c>NTransition.RoomFadeIn</c>): wait for the
/// background transition animation to finish before the run scene is revealed, so loading
/// overlaps the animation instead of leaving a black hold after it.
/// </summary>
public sealed class NinjaSlayerRoomFadeInGatePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_transition_room_fadein_gate";

    public static string Description => "Hold RoomFadeIn until the NinjaSlayer transition animation finishes.";

    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NTransition), nameof(NTransition.RoomFadeIn), [typeof(bool)])];

    public static bool Prefix(NTransition __instance, ref Task __result, bool showTransition)
    {
        if (!NinjaSlayerTransitionGate.TryClaimReveal(__instance, out NinjaSlayerTransitionSession? session))
        {
            return true;
        }

        __result = DelayThenReveal(__instance, session!, showTransition);
        return false;
    }

    private static async Task DelayThenReveal(
        NTransition transition,
        NinjaSlayerTransitionSession session,
        bool showTransition)
    {
        try
        {
            await session.WaitForAnimationAsync();
            session.ReleasePresentation();
            // ReleasePresentation unpauses the whole staged run tree and flushes every deferred
            // combat/audio operation at once. The backdrop is still opaque black here, so spend
            // that spike on an invisible frame instead of the first frame of the fade-in.
            await transition.AwaitProcessFrame();
            await transition.RoomFadeIn(showTransition);
            await session.CompleteAsync(TransitionCompletionStatus.Succeeded, forceRelease: false);
        }
        catch (OperationCanceledException)
        {
            await session.CompleteAsync(
                TransitionCompletionStatus.Cancelled,
                forceRelease: true,
                "RoomFadeIn was cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            await session.CompleteAsync(TransitionCompletionStatus.Faulted, forceRelease: true, ex.ToString());
            throw;
        }
    }
}

/// <summary>
/// Save-load reveal path (<c>NMainMenu</c> continue -> <c>NTransition.FadeIn</c>): same gating as
/// <see cref="NinjaSlayerRoomFadeInGatePatch"/> for the load flow.
/// </summary>
public sealed class NinjaSlayerFadeInGatePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_transition_fadein_gate";

    public static string Description => "Hold FadeIn until the NinjaSlayer transition animation finishes.";

    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NTransition), nameof(NTransition.FadeIn), [typeof(float), typeof(string), typeof(CancellationToken?)])];

    public static bool Prefix(NTransition __instance, ref Task __result, float time, string transitionPath, CancellationToken? cancelToken)
    {
        if (!NinjaSlayerTransitionGate.TryClaimReveal(__instance, out NinjaSlayerTransitionSession? session))
        {
            return true;
        }

        __result = DelayThenReveal(__instance, session!, time, transitionPath, cancelToken);
        return false;
    }

    private static async Task DelayThenReveal(
        NTransition transition,
        NinjaSlayerTransitionSession session,
        float time,
        string transitionPath,
        CancellationToken? cancelToken)
    {
        try
        {
            await session.WaitForAnimationAsync();
            session.ReleasePresentation();
            // Same reasoning as the embark path: absorb the unpause and deferred-operation flush
            // on a black frame before the fade-in starts animating.
            await transition.AwaitProcessFrame();
            await transition.FadeIn(time, transitionPath, cancelToken);
            await session.CompleteAsync(TransitionCompletionStatus.Succeeded, forceRelease: false);
        }
        catch (OperationCanceledException)
        {
            await session.CompleteAsync(
                TransitionCompletionStatus.Cancelled,
                forceRelease: true,
                "FadeIn was cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            await session.CompleteAsync(TransitionCompletionStatus.Faulted, forceRelease: true, ex.ToString());
            throw;
        }
    }
}
