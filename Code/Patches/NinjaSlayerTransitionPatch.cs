using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Code.Transition;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class NinjaSlayerTransitionPatch : IPatchMethod
{
    private static readonly PropertyInfo? InTransitionProperty =
        AccessTools.Property(typeof(NTransition), nameof(NTransition.InTransition));

    private static readonly FieldInfo? TweenField = AccessTools.Field(typeof(NTransition), "_tween");

    public static string PatchId => "ninjaslayer_character_transition";

    public static string Description => "Play NinjaSlayer transition frame animation during embark and save load.";

    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NTransition), nameof(NTransition.FadeOut), [typeof(float), typeof(string), typeof(CancellationToken?)])];

    public static bool Prefix(float time, string transitionPath, NTransition __instance, ref Task __result, CancellationToken? cancelToken = null)
    {
        if (!NinjaSlayerTransitionGate.Pending && !NinjaSlayerTransitionPaths.IsModPath(transitionPath))
        {
            return true;
        }

        NinjaSlayerTransitionGate.Pending = false;
        __result = PlayNinjaSlayerTransitionAsync(__instance, cancelToken);
        return false;
    }

    private static async Task PlayNinjaSlayerTransitionAsync(NTransition transition, CancellationToken? cancelToken)
    {
        if (SaveManager.Instance.PrefsSave.FastMode == FastModeType.Instant)
        {
            SetInTransition(transition, true);
            transition.Visible = false;
            return;
        }

        KillTransitionTween(transition);

        SetInTransition(transition, true);
        transition.Visible = true;
        transition.MouseFilter = Control.MouseFilterEnum.Stop;

        var gradientTransition = transition.GetNode<Control>("GradientTransition");
        gradientTransition.Modulate = new Color(1f, 1f, 1f, 0f);

        var simpleTransition = transition.GetNode<ColorRect>("SimpleTransition");
        simpleTransition.Color = Colors.Black;
        simpleTransition.Modulate = new Color(1f, 1f, 1f, 0f);

        var overlay = NinjaSlayerTransitionOverlay.GetOrCreate(transition);
        await overlay.PlayAsync(NinjaSlayerAudio.TransitionSeconds, cancelToken ?? CancellationToken.None);

        simpleTransition.Color = Colors.Black;
        simpleTransition.Modulate = Colors.Black;
        transition.MouseFilter = Control.MouseFilterEnum.Stop;
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
}
