using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using NinjaSlayer.Code.Nodes;

namespace NinjaSlayer.Code.Transition;

internal sealed class TransitionViewAdapter(NTransition transition)
{
    private const string SimpleTransitionPath = "SimpleTransition";
    private const string GradientTransitionPath = "GradientTransition";
    private static readonly PropertyInfo InTransition =
        AccessTools.Property(typeof(NTransition), nameof(NTransition.InTransition))
        ?? throw new MissingMemberException(
            typeof(NTransition).FullName,
            nameof(NTransition.InTransition));
    private static readonly FieldInfo TransitionTween =
        AccessTools.Field(typeof(NTransition), "_tween")
        ?? throw new MissingFieldException(typeof(NTransition).FullName, "_tween");
    private NinjaSlayerTransitionOverlay? _overlay;

    public NTransition Transition { get; } = transition;

    public void PrepareInstant()
    {
        EnsureValid();
        InTransition.SetValue(Transition, true);
        Transition.Visible = false;
    }

    public NinjaSlayerTransitionOverlay PrepareAnimated()
    {
        EnsureValid();
        if (ReadTransitionTween() is { } tween)
        {
            tween.Kill();
            TransitionTween.SetValue(Transition, null);
        }
        InTransition.SetValue(Transition, true);
        Transition.Visible = true;
        Transition.MouseFilter = Control.MouseFilterEnum.Stop;

        Control gradient = Transition.GetNode<Control>(GradientTransitionPath);
        gradient.Modulate = new Color(1f, 1f, 1f, 0f);

        ColorRect backdrop = Transition.GetNode<ColorRect>(SimpleTransitionPath);
        SetBackdrop(backdrop, opaque: true);

        _overlay = NinjaSlayerTransitionOverlay.GetOrCreate(Transition);
        return _overlay;
    }

    public void HoldBackdrop()
    {
        if (GetBackdrop() is { } backdrop)
        {
            SetBackdrop(backdrop, opaque: true);
        }
    }

    public void StopPlayback()
    {
        if (_overlay is not null && GodotObject.IsInstanceValid(_overlay))
        {
            _overlay.StopPlayback();
        }
    }

    public void Restore(bool forceRelease)
    {
        if (!GodotObject.IsInstanceValid(Transition))
        {
            return;
        }

        if (forceRelease)
        {
            if (ReadTransitionTween() is { } tween)
            {
                tween.Kill();
                TransitionTween.SetValue(Transition, null);
            }
            if (GetBackdrop() is { } backdrop)
            {
                SetBackdrop(backdrop, opaque: false);
            }

            if (Transition.GetNodeOrNull<Control>(GradientTransitionPath) is { } gradient)
            {
                gradient.Modulate = new Color(1f, 1f, 1f, 0f);
            }

            Transition.Visible = false;
        }

        Transition.MouseFilter = Control.MouseFilterEnum.Ignore;
        InTransition.SetValue(Transition, false);
    }

    private ColorRect? GetBackdrop() =>
        GodotObject.IsInstanceValid(Transition)
            ? Transition.GetNodeOrNull<ColorRect>(SimpleTransitionPath)
            : null;

    private void EnsureValid()
    {
        if (!GodotObject.IsInstanceValid(Transition))
        {
            throw new InvalidOperationException("The transition view is no longer valid.");
        }
    }

    private Tween? ReadTransitionTween() =>
        TransitionTween.GetValue(Transition) switch
        {
            null => null,
            Tween tween => tween,
            _ => throw new InvalidOperationException(
                "NTransition._tween has an unexpected runtime type.")
        };

    private static void SetBackdrop(ColorRect backdrop, bool opaque)
    {
        backdrop.Color = Colors.Black;
        backdrop.Modulate = new Color(1f, 1f, 1f, opaque ? 1f : 0f);
    }
}
