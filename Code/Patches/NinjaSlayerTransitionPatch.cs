using System;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Code.Compatibility;
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

        // Start the transition video in the background and return immediately so the caller's
        // run/save asset loading overlaps the animation instead of producing a black hold
        // afterwards. The reveal patches (RoomFadeIn/FadeIn) await this task before showing.
        if (!NinjaSlayerTransitionGate.TryStartSession(
                __instance,
                cancelToken ?? CancellationToken.None,
                (session, token) => BeginNinjaSlayerTransition(session, __instance, token),
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
        NTransition transition,
        CancellationToken cancelToken)
    {
        if (SaveManager.Instance.PrefsSave.FastMode == FastModeType.Instant)
        {
            GameCompatibility.Transition.SetInTransition(transition, true);
            transition.Visible = false;
            return Task.CompletedTask;
        }

        // Cover the screen synchronously before returning so the character select / menu never
        // flashes through while the video decoder produces its first frame.
        GameCompatibility.Transition.KillTween(transition);

        GameCompatibility.Transition.SetInTransition(transition, true);
        transition.Visible = true;
        transition.MouseFilter = Control.MouseFilterEnum.Stop;

        var gradientTransition = transition.GetNode<Control>("GradientTransition");
        gradientTransition.Modulate = new Color(1f, 1f, 1f, 0f);

        var simpleTransition = transition.GetNode<ColorRect>("SimpleTransition");
        simpleTransition.Color = Colors.Black;
        simpleTransition.Modulate = new Color(1f, 1f, 1f, 1f);

        var overlay = NinjaSlayerTransitionOverlay.GetOrCreate(transition);
        session.OwnOverlay(overlay);
        if (NinjaSlayerPatchCapabilities.TransitionLoadSmoothingEnabled)
        {
            session.BeginLoadSmoothing();
        }
        return PlayOverlayAsync(session, overlay, simpleTransition, cancelToken);
    }

    private static async Task PlayOverlayAsync(
        NinjaSlayerTransitionSession session,
        NinjaSlayerTransitionOverlay overlay,
        ColorRect simpleTransition,
        CancellationToken cancelToken)
    {
        try
        {
            await overlay.PlayAsync(NinjaSlayerAudio.TransitionVisualSeconds, cancelToken);
        }
        finally
        {
            // Keep the screen covered with opaque black until the reveal patch clears/fades it.
            if (session.ShouldHoldBackdrop && GodotObject.IsInstanceValid(simpleTransition))
            {
                simpleTransition.Color = Colors.Black;
                simpleTransition.Modulate = new Color(1f, 1f, 1f, 1f);
            }

            session.EndLoadSmoothing();
        }
    }
}
