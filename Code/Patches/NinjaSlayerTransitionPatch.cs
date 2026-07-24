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

        // Start the transition video in the background and return immediately so the caller's
        // run/save asset loading overlaps the animation instead of producing a black hold
        // afterwards. The reveal patches (RoomFadeIn/FadeIn) await this task before showing.
        if (!NinjaSlayerTransitionGate.TryStartSession(
                __instance,
                invocationKind,
                cancelToken ?? CancellationToken.None,
                BeginNinjaSlayerTransition,
                out _))
        {
            return true;
        }

        float delay = wasPending
            ? NinjaSlayerAudio.EmbarkLoadStartDelaySeconds
            : NinjaSlayerAudio.SaveLoadStartDelaySeconds;
        __result = delay > 0f
            ? Cmd.Wait(delay, cancelToken ?? CancellationToken.None)
            : Task.CompletedTask;
        return false;
    }

    private static Task BeginNinjaSlayerTransition(
        NinjaSlayerTransitionSession session,
        CancellationToken cancelToken)
    {
        if (SaveManager.Instance.PrefsSave.FastMode == FastModeType.Instant)
        {
            session.PrepareInstantView();
            return Task.CompletedTask;
        }

        // Cover the screen synchronously before returning so the character select / menu never
        // flashes through while the video decoder produces its first frame.
        if (NinjaSlayerPatchCapabilities.TransitionLoadSmoothingEnabled)
        {
            session.BeginLoadSmoothing();
        }
        NinjaSlayerTransitionOverlay overlay = session.PrepareAnimatedView();
        return PlayOverlayAsync(session, overlay, cancelToken);
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

            session.EndAnimationSmoothing();
        }
    }
}
