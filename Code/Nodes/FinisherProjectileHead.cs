using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace NinjaSlayer.Code.Nodes;

internal sealed partial class FinisherProjectileHead : Sprite2D
{
    internal Vector2 Origin { get; init; }
    internal NCreature Target { get; init; } = null!;
    internal bool Spins { get; set; }
    private float _elapsed;
    private bool _arrived;
    private float _angle;

    public override void _Ready()
    {
        RenderingServer.FramePreDraw += SyncPosition;
        SyncPosition();
    }

    public override void _Process(double delta)
    {
        if (_arrived) return;
        _elapsed += (float)delta;
        if (Spins) _angle += (float)delta * Mathf.Tau / 0.15f;
        SyncPosition();
    }

    internal void Arrive()
    {
        _arrived = true;
        SyncPosition();
        // The native spray remains a flight trail, never a second projectile
        // continuing through the victim during the impact hold.
        GetParent().GetNode<Node2D>("throw_container").Hide();
    }

    private void SyncPosition()
    {
        if (!GodotObject.IsInstanceValid(Target) || !Target.IsInsideTree()) return;
        Vector2 destination = Target.VfxSpawnPosition;
        GlobalPosition = Origin.Lerp(destination, _arrived ? 1f : Mathf.Clamp(_elapsed / 0.15f, 0f, 1f));
        if (!_arrived) GlobalRotation = Spins ? _angle : (destination - Origin).Angle() + Mathf.Pi * 0.5f;
    }

    public override void _ExitTree() => RenderingServer.FramePreDraw -= SyncPosition;
}
