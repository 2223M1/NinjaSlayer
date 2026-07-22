using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
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
    private static readonly PropertyInfo? InTransitionProperty =
        AccessTools.Property(typeof(NTransition), nameof(NTransition.InTransition));

    private static readonly FieldInfo? TweenField = AccessTools.Field(typeof(NTransition), "_tween");

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

        bool wasPending = NinjaSlayerTransitionGate.Pending;
        NinjaSlayerTransitionGate.Pending = false;

        // Start the transition video in the background and return immediately so the caller's
        // run/save asset loading overlaps the animation instead of producing a black hold
        // afterwards. The reveal patches (RoomFadeIn/FadeIn) await this task before showing.
        NinjaSlayerTransitionGate.StartAnimation(
            __instance,
            cancelToken ?? CancellationToken.None,
            token => BeginNinjaSlayerTransition(__instance, token));

        float delay = wasPending
            ? NinjaSlayerAudio.EmbarkLoadStartDelaySeconds
            : NinjaSlayerAudio.SaveLoadStartDelaySeconds;
        __result = delay > 0f
            ? Cmd.Wait(delay, cancelToken ?? CancellationToken.None)
            : Task.CompletedTask;
        return false;
    }

    private static Task BeginNinjaSlayerTransition(NTransition transition, CancellationToken cancelToken)
    {
        if (SaveManager.Instance.PrefsSave.FastMode == FastModeType.Instant)
        {
            SetInTransition(transition, true);
            transition.Visible = false;
            return Task.CompletedTask;
        }

        // Cover the screen synchronously before returning so the character select / menu never
        // flashes through while the video decoder produces its first frame.
        KillTransitionTween(transition);

        SetInTransition(transition, true);
        transition.Visible = true;
        transition.MouseFilter = Control.MouseFilterEnum.Stop;

        var gradientTransition = transition.GetNode<Control>("GradientTransition");
        gradientTransition.Modulate = new Color(1f, 1f, 1f, 0f);

        var simpleTransition = transition.GetNode<ColorRect>("SimpleTransition");
        simpleTransition.Color = Colors.Black;
        simpleTransition.Modulate = new Color(1f, 1f, 1f, 1f);

        var overlay = NinjaSlayerTransitionOverlay.GetOrCreate(transition);
        if (NinjaSlayerPatchCapabilities.TransitionLoadSmoothingEnabled)
        {
            NinjaSlayerTransitionLoadSmoothing.BeginAnimation();
        }
        return PlayOverlayAsync(overlay, simpleTransition, cancelToken);
    }

    private static async Task PlayOverlayAsync(NinjaSlayerTransitionOverlay overlay, ColorRect simpleTransition, CancellationToken cancelToken)
    {
        try
        {
            await overlay.PlayAsync(NinjaSlayerAudio.TransitionVisualSeconds, cancelToken);
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"NinjaSlayer transition animation failed: {ex}");
        }
        finally
        {
            // Keep the screen covered with opaque black until the reveal patch clears/fades it.
            if (GodotObject.IsInstanceValid(simpleTransition))
            {
                simpleTransition.Color = Colors.Black;
                simpleTransition.Modulate = new Color(1f, 1f, 1f, 1f);
            }

            if (NinjaSlayerPatchCapabilities.TransitionLoadSmoothingEnabled)
            {
                NinjaSlayerTransitionLoadSmoothing.EndAnimationAndCollectDeferred();
            }
        }
    }

    private static void KillTransitionTween(NTransition transition)
    {
        if (TweenField?.GetValue(transition) is Tween tween)
        {
            tween.Kill();
            TweenField.SetValue(transition, null);
        }
    }

    private static void SetInTransition(NTransition transition, bool value)
    {
        InTransitionProperty?.SetValue(transition, value);
    }

    internal static void ReleaseTransitionInput(NTransition transition)
    {
        if (!GodotObject.IsInstanceValid(transition))
        {
            return;
        }

        transition.MouseFilter = Control.MouseFilterEnum.Ignore;
        SetInTransition(transition, false);
    }

    internal static void ForceReleaseTransition(NTransition transition)
    {
        if (!GodotObject.IsInstanceValid(transition))
        {
            return;
        }

        if (transition.GetNodeOrNull<ColorRect>("SimpleTransition") is { } simpleTransition)
        {
            simpleTransition.Modulate = new Color(1f, 1f, 1f, 0f);
        }

        transition.Visible = false;
        ReleaseTransitionInput(transition);
    }
}
