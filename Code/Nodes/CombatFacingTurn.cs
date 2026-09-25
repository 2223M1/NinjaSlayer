using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Monsters;

namespace NinjaSlayer.Code.Nodes;

internal sealed partial class CombatFacingTurn : Node
{
    private NCreature _actor = null!;
    private Node2D _body = null!;
    private VerticalAxisSpinProjection? _projection;
    private EntangledSpinMotionBlur? _blur;
    private Transform2D _projected;
    private bool _initialized, _targetLeft;
    private float _angle, _from, _to, _elapsed, _duration;

    internal static CombatFacingTurn Ensure(NCreature actor)
    {
        if (actor.Visuals.GetNodeOrNull<CombatFacingTurn>(nameof(CombatFacingTurn)) is { } existing) return existing;
        var turn = new CombatFacingTurn
        {
            Name = nameof(CombatFacingTurn), _actor = actor,
            _body = NinjaSlayerVisualRig.GetAirborneAnchor(actor.Visuals) ?? actor.Body,
            ProcessPriority = -1100
        };
        actor.Visuals.AddChild(turn);
        return turn;
    }

    public override void _Ready() => RenderingServer.FramePreDraw += DrawTurn;

    internal void SetFacing(bool left, bool immediate = false)
    {
        if (!_initialized || immediate || CombatActionTimingRuntime.VisualSeconds(1f) <= 0f)
        {
            StopProjection();
            _blur?.Stop();
            _blur = null;
            _initialized = true;
            _targetLeft = left;
            _angle = _from = _to = left ? 180f : 0f;
            _duration = 0f;
            ApplyFacing(left);
            return;
        }
        if (_targetLeft == left)
        {
            if (_duration <= 0f) ApplyFacing(left);
            return;
        }
        _targetLeft = left;
        _from = _angle;
        _to = left ? 180f : 0f;
        _elapsed = 0f;
        _duration = DragPoseMath.TurnSeconds;
    }

    public override void _Process(double delta)
    {
        // Remove only our render-time projection before the action writers run.
        StopProjection();
        if (_actor.Entity.IsDead)
        {
            _blur?.Stop();
            _blur = null;
            _duration = 0f;
            return;
        }
        if (_duration <= 0f) return;
        _elapsed = Math.Min(_duration, _elapsed + (float)delta);
        _angle = DragPoseMath.TurnAngle(_from, _to, _elapsed, _duration);
        ApplyFacing(_angle > 90f || Mathf.IsEqualApprox(_angle, 90f) && _to > _from);
        if (_elapsed >= _duration)
        {
            _duration = 0f;
            _blur?.Stop();
            _blur = null;
        }
    }

    private void ApplyFacing(bool left)
    {
        Node2D drawing = _actor.Body;
        if (_actor.Entity.Monster is SawatariMonster)
        {
            var sprite = (Sprite2D)drawing;
            bool changed = sprite.FlipH != !left;
            sprite.Transform = YamotoKokiAllyFacingController.WithFacing(sprite.Transform, false);
            sprite.FlipH = !left;
            if (changed) SawatariWeaponVisuals.Get(_actor.Entity)?.SyncBody();
        }
        else drawing.Transform = YamotoKokiAllyFacingController.WithFacing(drawing.Transform, left);
        NinjaSlayerShadowController.Get(_actor.Entity)?.SetMirrored(
            _actor.Entity.Monster is SawatariMonster ? !left : left);
    }

    private void DrawTurn()
    {
        if (!IsInsideTree() || !CanProcess() || _duration <= 0f || _actor.Entity.IsDead) return;
        StopProjection();
        if (_blur == null)
        {
            Rect2 bounds = _body.GetGlobalTransformWithCanvas().AffineInverse()
                * _actor.Visuals.Bounds.GetGlobalTransformWithCanvas()
                * new Rect2(Vector2.Zero, _actor.Visuals.Bounds.Size);
            foreach (Sprite2D sprite in Sprites(_body).Where(s => s.IsVisibleInTree() && s.Texture != null))
                bounds = bounds.Merge(_body.GetGlobalTransformWithCanvas().AffineInverse()
                    * sprite.GetGlobalTransformWithCanvas() * sprite.GetRect());
            _blur = EntangledSpinMotionBlur.Create(_body, bounds);
        }
        Vector2 pivot = _actor.Visuals.VfxSpawnPosition.GetGlobalTransformWithCanvas().Origin;
        _projection = VerticalAxisSpinProjection.CaptureCurrent(_body, pivot.X, pivot);
        float basis = _angle > 90f || Mathf.IsEqualApprox(_angle, 90f) && _to > _from ? 180f : 0f;
        float degrees = _angle - basis;
        float from = _from, to = _to, elapsed = _elapsed, duration = _duration;
        double Before(double age) => DragPoseMath.TurnAngle(from, to, Math.Max(0f, elapsed - (float)age), duration);
        _projection.ApplyDegrees(degrees);
        _projected = _body.Transform;
        _blur.Record(_projection, degrees, Before, basis);
        _blur.SyncNow();
    }

    private void StopProjection()
    {
        if (_projection == null) return;
        // A new death/grapple pose may have replaced the old draw transform.
        Transform2D current = _body.Transform;
        bool stillOwned = current.IsEqualApprox(_projected);
        _projection.Restore();
        if (!stillOwned) _body.Transform = current;
        _projection = null;
    }

    private static IEnumerable<Sprite2D> Sprites(Node node)
    {
        if (node is Sprite2D sprite) yield return sprite;
        foreach (Node child in node.GetChildren())
            foreach (Sprite2D descendant in Sprites(child)) yield return descendant;
    }

    public override void _ExitTree()
    {
        RenderingServer.FramePreDraw -= DrawTurn;
        if (GodotObject.IsInstanceValid(_body)) StopProjection();
        _blur?.Stop();
        _blur = null;
    }
}
