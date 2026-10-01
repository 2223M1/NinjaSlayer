using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace NinjaSlayer.SmokeDriver;

// Observe native scene transforms; cinematic canvas zoom is intentionally excluded.
internal sealed class FinisherUiProbe : IDisposable
{
    private readonly NCreature _actor;
    private readonly Control _health;
    private readonly Control _powers;
    private readonly Transform2D _root, _healthStart, _powersStart;
    private readonly Vector2 _bodyStart;
    private string? _failure;
    private int _frames;
    private bool _bodyMoved;

    internal FinisherUiProbe(NCreature actor)
    {
        _actor = actor;
        _health = actor.GetNode<Control>("%HealthBar");
        _powers = _health.GetNode<Control>("%PowerContainer");
        _root = actor.GetTransform();
        _healthStart = RelativeToLayout(_health);
        _powersStart = RelativeToLayout(_powers);
        _bodyStart = LayoutInverse() * actor.VfxSpawnPosition;
        RenderingServer.FramePreDraw += Sample;
    }

    private Transform2D LayoutInverse() => _actor.GetParent<CanvasItem>().GetGlobalTransform().AffineInverse();
    private Transform2D RelativeToLayout(CanvasItem node) => LayoutInverse() * node.GetGlobalTransform();

    private void Sample()
    {
        if (!GodotObject.IsInstanceValid(_actor) || !_actor.IsInsideTree()) return;
        _frames++;
        if (!_actor.GetTransform().IsEqualApprox(_root))
            _failure ??= $"creature root moved: {_root.Origin} -> {_actor.Position}";
        if (!RelativeToLayout(_health).IsEqualApprox(_healthStart)) _failure ??= "health bar moved";
        if (!RelativeToLayout(_powers).IsEqualApprox(_powersStart)) _failure ??= "status container moved";
        _bodyMoved |= (LayoutInverse() * _actor.VfxSpawnPosition).DistanceTo(_bodyStart) > 10f;
    }

    internal void Verify()
    {
        Sample();
        if (_failure != null || _frames < 2 || !_bodyMoved)
            throw new InvalidOperationException($"Finisher UI: {_failure ?? "missing body motion"}; sampled {_frames} frames.");
        GD.Print($"PASS finisher stationary native health/status UI with body motion: {_frames} frames.");
    }

    public void Dispose() => RenderingServer.FramePreDraw -= Sample;
}
