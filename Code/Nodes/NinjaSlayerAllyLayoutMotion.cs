using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.ExternalAnimations;

namespace NinjaSlayer.Code.Nodes;

internal sealed partial class NinjaSlayerAllyLayoutMotion : Node
{
    private const float Duration = .25f;
    private NCreature _actor = null!;
    private Vector2 _start, _destination;
    private int _companions;
    private float _elapsed;
    private bool _presented, _pending, _waitingForAction;

    internal static NinjaSlayerAllyLayoutMotion Ensure(NCreature actor)
    {
        if (actor.GetNodeOrNull<NinjaSlayerAllyLayoutMotion>(nameof(NinjaSlayerAllyLayoutMotion)) is { } existing)
            return existing;
        var motion = new NinjaSlayerAllyLayoutMotion
        {
            Name = nameof(NinjaSlayerAllyLayoutMotion), _actor = actor, ProcessPriority = -1200
        };
        actor.AddChild(motion);
        return motion;
    }

    internal void OnLayout(Vector2 previousPosition, int companions)
    {
        Vector2 destination = _actor.Position;
        bool changed = companions != _companions;
        _companions = companions;
        if (!_presented || !_actor.IsVisibleInTree() || CombatActionTimingRuntime.VisualSeconds(1f) <= 0f)
        {
            _destination = destination;
            _pending = false;
            _waitingForAction = false;
            return;
        }

        // Native layout may run several times for one pet change. Keep the same
        // in-flight destination without restarting the easing or snapping the actor.
        bool sameDestination = destination.IsEqualApprox(_destination);
        if (changed || _pending || ActionOwnsPosition)
            _actor.Position = previousPosition;
        _destination = destination;
        if (_pending && sameDestination) return;
        if (!changed && !_pending) return;
        _start = previousPosition;
        _elapsed = 0f;
        _waitingForAction = false;
        _pending = !previousPosition.IsEqualApprox(destination);
    }

    private bool ActionOwnsPosition =>
        NinjaSlayerRapidAnimationCoordinator.HasActiveMotion(_actor.Entity)
        || NinjaSlayerFinisherCinematic.IsMovementOwned(_actor.Entity)
        || NinjaSlayerAimPose.Get(_actor.Entity) is { IsBusy: true } or { IsReturning: true };

    public override void _Process(double delta)
    {
        _presented = true;
        if (!_pending || CombatManager.Instance.IsPaused) return;
        if (_actor.Entity.IsDead || !_actor.IsVisibleInTree())
        {
            _pending = false;
            return;
        }
        if (ActionOwnsPosition)
        {
            // An attack starting during layout movement retains its own baseline.
            // Resume from its completed return, never write over a lunge or Doom.
            _waitingForAction = true;
            _elapsed = 0f;
            return;
        }
        if (_waitingForAction)
        {
            _start = _actor.Position;
            _waitingForAction = false;
        }
        _elapsed = Math.Min(Duration, _elapsed + (float)delta);
        _actor.Position = _start.Lerp(_destination, Mathf.SmoothStep(0f, 1f, _elapsed / Duration));
        if (_elapsed >= Duration) _pending = false;
    }
}
