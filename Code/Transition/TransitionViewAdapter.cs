using Godot;
using MegaCrit.Sts2.Core.Nodes;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Code.Nodes;

namespace NinjaSlayer.Code.Transition;

internal interface ITransitionViewAdapter
{
    NTransition Transition { get; }
    void PrepareInstant();
    NinjaSlayerTransitionOverlay PrepareAnimated(TransitionPerformanceTrace? performanceTrace);
    void HoldBackdrop();
    void StopPlayback();
    void DetachPerformanceTrace(TransitionPerformanceTrace performanceTrace);
    void Restore(bool forceRelease);
}

internal sealed class TransitionViewAdapter(NTransition transition) : ITransitionViewAdapter
{
    private const string SimpleTransitionPath = "SimpleTransition";
    private const string GradientTransitionPath = "GradientTransition";
    private NinjaSlayerTransitionOverlay? _overlay;

    public NTransition Transition { get; } = transition;

    public void PrepareInstant()
    {
        EnsureValid();
        GameCompatibility.Transition.SetInTransition(Transition, true);
        Transition.Visible = false;
    }

    public NinjaSlayerTransitionOverlay PrepareAnimated(TransitionPerformanceTrace? performanceTrace)
    {
        EnsureValid();
        GameCompatibility.Transition.KillTween(Transition);
        GameCompatibility.Transition.SetInTransition(Transition, true);
        Transition.Visible = true;
        Transition.MouseFilter = Control.MouseFilterEnum.Stop;

        Control gradient = Transition.GetNode<Control>(GradientTransitionPath);
        gradient.Modulate = new Color(1f, 1f, 1f, 0f);

        ColorRect backdrop = Transition.GetNode<ColorRect>(SimpleTransitionPath);
        SetBackdrop(backdrop, opaque: true);

        _overlay = NinjaSlayerTransitionOverlay.GetOrCreate(Transition);
        if (performanceTrace is not null)
        {
            _overlay.AttachPerformanceTrace(performanceTrace);
        }
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

    public void DetachPerformanceTrace(TransitionPerformanceTrace performanceTrace)
    {
        if (_overlay is not null && GodotObject.IsInstanceValid(_overlay))
        {
            _overlay.DetachPerformanceTrace(performanceTrace);
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
            GameCompatibility.Transition.KillTween(Transition);
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
        GameCompatibility.Transition.SetInTransition(Transition, false);
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

    private static void SetBackdrop(ColorRect backdrop, bool opaque)
    {
        backdrop.Color = Colors.Black;
        backdrop.Modulate = new Color(1f, 1f, 1f, opaque ? 1f : 0f);
    }
}
