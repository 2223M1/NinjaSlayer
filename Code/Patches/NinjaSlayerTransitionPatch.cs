using System;
using System.Threading;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Code.Transition;
using NinjaSlayer.Content;
using NinjaSlayer.Scripts;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class NinjaSlayerTransitionPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_character_transition";

    public static string Description => "Play the NinjaSlayer transition video during embark and save load.";

    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NTransition), nameof(NTransition.FadeOut), [typeof(float), typeof(string), typeof(CancellationToken?)])];

    public static bool Prefix(float time, string transitionPath, NTransition __instance, ref Task __result, CancellationToken? cancelToken = null)
    {
        if (!NinjaSlayerPatchCapabilities.TransitionEnabled ||
            (!NinjaSlayerTransitionGate.Pending && !NinjaSlayerTransitionPaths.IsModPath(transitionPath)))
        {
            return true;
        }

        bool wasPending = NinjaSlayerTransitionGate.ConsumePendingRequest();
        TransitionInvocationKind invocationKind = wasPending
            ? TransitionInvocationKind.Embark
            : TransitionInvocationKind.SaveLoad;

        // Start playback independently, then release loading at the established media cue.
        // The reveal patches (RoomFadeIn/FadeIn) still await the full presentation task.
        if (!NinjaSlayerTransitionGate.TryStartSession(
                __instance,
                invocationKind,
                BeginNinjaSlayerTransition,
                cancelToken ?? CancellationToken.None,
                out NinjaSlayerTransitionSession? session))
        {
            return true;
        }

        float delay = wasPending
            ? NinjaSlayerAudio.EmbarkLoadStartDelaySeconds
            : NinjaSlayerAudio.SaveLoadStartDelaySeconds;
        if (NinjaSlayerPatchCapabilities.TransitionPresentationEnabled)
        {
            __result = WaitForViewReadyAndLoadDelayAsync(
                session!,
                delay,
                cancelToken ?? CancellationToken.None);
        }
        else
        {
            __result = WaitForLoadDelayAsync(
                delay,
                cancelToken ?? CancellationToken.None);
        }
        return false;
    }

    private static async Task WaitForViewReadyAndLoadDelayAsync(
        NinjaSlayerTransitionSession session,
        float delay,
        CancellationToken cancellationToken)
    {
        await session.WaitForViewReadyAsync(cancellationToken);
        if (delay > 0f)
        {
            await Cmd.Wait(delay, cancellationToken);
        }
    }

    private static async Task WaitForLoadDelayAsync(
        float delay,
        CancellationToken cancellationToken)
    {
        if (delay > 0f)
        {
            await Cmd.Wait(delay, cancellationToken);
        }
    }

    private static async Task BeginNinjaSlayerTransition(
        NinjaSlayerTransitionSession session,
        CancellationToken cancelToken)
    {
        if (SaveManager.Instance.PrefsSave.FastMode == FastModeType.Instant)
        {
            session.PrepareInstantView();
            return;
        }

        if (NinjaSlayerPatchCapabilities.TransitionLoadSmoothingEnabled)
        {
            session.BeginLoadSmoothing();
        }
        NinjaSlayerTransitionOverlay overlay = session.PrepareAnimatedView();
        await PlayOverlayAsync(session, overlay, cancelToken);
    }

    private static async Task PlayOverlayAsync(
        NinjaSlayerTransitionSession session,
        NinjaSlayerTransitionOverlay overlay,
        CancellationToken cancelToken)
    {
        try
        {
            await overlay.PlayAsync(NinjaSlayerAudio.TransitionVisualSeconds, cancelToken);
        }
        finally
        {
            // Keep the screen covered with opaque black until the reveal patch clears/fades it.
            if (session.ShouldHoldBackdrop)
            {
                session.HoldBackdrop();
            }

            // Load smoothing deliberately stays armed past the end of the video. Dropping it here
            // restored the vanilla 128-wide asset concurrency and unbounded finalize drain in the
            // same frames as the fade-in, which is exactly where the hitch was visible. The
            // session ends it during CompleteAsync, after the reveal has finished.
        }
    }
}
