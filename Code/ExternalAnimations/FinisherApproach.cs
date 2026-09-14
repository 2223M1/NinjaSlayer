using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.ExternalAnimations;

internal sealed class FinisherApproach : IDisposable
{
    private static readonly Dictionary<Creature, FinisherApproach> Active = [];
    private readonly NCreature _actor;
    private readonly Vector2 _destination;
    private Vector2 _animationPosition;
    private Vector2 _offset;
    private Vector2 _returnFrom;
    private Vector2 _from;
    private Tween? _tween;
    private bool _claimed;
    private bool _disposed;
    private bool _returning;

    internal float? ReturnDuration { get; set; }

    internal static bool IsActive(Creature actor) =>
        Active.TryGetValue(actor, out var lease) && !lease._returning;

    internal static void ReachImpact(Creature actor)
    {
        if (Active.TryGetValue(actor, out var lease) && !lease._returning) lease.ApplyProgress(1f);
    }

    private FinisherApproach(NCreature actor, NCreature focus, Vector2 squash)
    {
        _actor = actor;
        _animationPosition = actor.Visuals.Position;
        float impactX = FinisherImpactPositionResolver.ResolveImpactX(actor, focus, squash,
            NinjaSlayerCombatVisuals.CloseRangeApproachGap);
        Vector2 canvasTravel = actor.GetParent<CanvasItem>().GetGlobalTransformWithCanvas()
            .BasisXform(new Vector2(impactX - actor.Position.X, 0f));
        _destination = actor.Visuals.GetParent<CanvasItem>().GetGlobalTransformWithCanvas().AffineInverse().BasisXform(canvasTravel);
        actor.TreeExiting += Dispose;
    }

    internal static FinisherApproach Create(NCreature actor, NCreature focus, Vector2 squash)
    {
        Vector2 baseline = actor.Visuals.Position, offset = Vector2.Zero;
        if (Active.TryGetValue(actor.Entity, out var previous))
        {
            baseline = previous._animationPosition;
            offset = previous._offset;
            previous.Dispose();
        }
        var lease = new FinisherApproach(actor, focus, squash);
        lease._animationPosition = baseline;
        lease._from = offset;
        lease.Apply(offset);
        Active[actor.Entity] = lease;
        return lease;
    }

    internal static bool TryPlayToPeak(Creature actor, float seconds, out Task action)
    {
        action = Task.CompletedTask;
        if (!Active.TryGetValue(actor, out var lease) || lease._disposed || lease._returning) return false;
        action = Cmd.Wait(seconds);
        return true;
    }

    internal static Vector2 AnimationPosition(Creature actor, Node2D visuals) =>
        Active.TryGetValue(actor, out var lease) ? lease._animationPosition : visuals.Position;

    // Existing recovery/shake channels retain their own baselines. Only this
    // compositor adds the approach; no runtime reparenting or layout-root writes.
    internal static void SetAnimationPosition(Creature actor, Node2D visuals, Vector2 position)
    {
        if (Active.TryGetValue(actor, out var lease))
        {
            lease._animationPosition = position;
            visuals.Position = position + lease._offset;
        }
        else visuals.Position = position;
    }

    internal void Start(float seconds)
    {
        if (seconds <= 0f) { ApplyProgress(1f); return; }
        _tween = _actor.CreateTween();
        _tween.TweenMethod(Callable.From<float>(ApplyProgress), 0f, 1f, seconds);
    }

    internal void ApplyProgress(float progress) =>
        Apply(_from.Lerp(_destination, CombatCinematicCameraLease.EaseOutCubic(Mathf.Clamp(progress, 0f, 1f))));

    private void Apply(Vector2 offset)
    {
        if (_disposed || !GodotObject.IsInstanceValid(_actor.Visuals)) return;
        _offset = offset;
        _actor.Visuals.Position = _animationPosition + offset;
    }

    internal static FinisherApproach? Claim(Creature actor)
    {
        if (!Active.TryGetValue(actor, out var lease) || lease._disposed || lease._returning) return null;
        lease._claimed = true;
        lease._tween?.Kill();
        return lease;
    }

    internal void BeginReturn()
    {
        _returning = true;
        _tween?.Kill();
        _returnFrom = _offset;
    }

    internal void ApplyReturn(float progress) => Apply(_returnFrom.Lerp(Vector2.Zero, Mathf.Clamp(progress, 0f, 1f)));

    internal void ReleasePrediction()
    {
        if (_claimed || _disposed) return;
        if (_actor.Entity.IsDead) { Dispose(); return; }
        BeginReturn();
        float seconds = ReturnDuration ?? .2f;
        if (seconds <= 0f) { Dispose(); return; }
        _tween = _actor.CreateTween();
        _tween.TweenMethod(Callable.From<float>(p => ApplyReturn(Mathf.SmoothStep(0f, 1f, p))), 0f, 1f, seconds);
        _tween.TweenCallback(Callable.From(Dispose));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _tween?.Kill();
        Apply(Vector2.Zero);
        _disposed = true;
        if (Active.GetValueOrDefault(_actor.Entity) == this) Active.Remove(_actor.Entity);
        if (GodotObject.IsInstanceValid(_actor)) _actor.TreeExiting -= Dispose;
    }
}
