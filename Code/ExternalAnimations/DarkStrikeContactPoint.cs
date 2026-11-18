using Godot;

namespace NinjaSlayer.Code.ExternalAnimations;

// The victim owns the point, the room owns the swordsman. Removing a victim
// mid-stab must never remove the attacker or take its return animation with it.
internal sealed partial class DarkStrikeContactPoint : Node2D
{
    private Node2D? _swordsman;
    private Vector2 _gripOffset;

    internal void Hold(Node2D swordsman)
    {
        _swordsman = swordsman;
        _gripOffset = swordsman.Position - PositionInStage(swordsman);
        SetNotifyTransform(true);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationTransformChanged && IsInsideTree()
            && GodotObject.IsInstanceValid(_swordsman) && _swordsman!.IsInsideTree())
            _swordsman.Position = PositionInStage(_swordsman) + _gripOffset;
    }

    private Vector2 PositionInStage(Node2D swordsman) =>
        swordsman.GetParent<CanvasItem>().GetGlobalTransformWithCanvas().AffineInverse()
        * GetGlobalTransformWithCanvas().Origin;

    internal void Release()
    {
        _swordsman = null;
        QueueFree();
    }
}
