using Godot;
using NinjaSlayer.Code.Nodes;

namespace NinjaSlayer.Code.ExternalAnimations;

internal sealed class VerticalAxisSpinProjection
{
    private readonly Node2D _body;
    private readonly CanvasItem _parent;
    private readonly Vector2 _markerBodyLocal;
    private readonly Vector2 _markerParentLocal;
    private readonly float _axisParentX;
    private readonly Vector2 _basePosition;
    private readonly float _baseRotationDegrees;
    private readonly Vector2 _baseScale;
    private readonly Vector2 _baseOffset;
    private readonly NinjaSlayerShadowController? _shadow;
    private readonly NinjaSlayerSpinMotionBlur? _blur;
    private readonly NinjaSlayerAimPose? _aim;

    private VerticalAxisSpinProjection(
        Node2D body,
        CanvasItem parent,
        Vector2 markerBodyLocal,
        Vector2 markerParentLocal,
        float axisParentX,
        Vector2 basePosition,
        float baseRotationDegrees,
        Vector2 baseScale)
    {
        _body = body;
        _aim = body.GetParent() as NinjaSlayerAimPose;
        _parent = parent;
        _markerBodyLocal = markerBodyLocal;
        _markerParentLocal = markerParentLocal;
        _axisParentX = axisParentX;
        _basePosition = basePosition;
        _baseRotationDegrees = baseRotationDegrees;
        _baseScale = baseScale;
        _baseOffset = body is Sprite2D sprite ? sprite.Offset : Vector2.Zero;
        for (Node? node = body.GetParent(); node != null; node = node.GetParent())
        {
            _blur ??= node.GetNodeOrNull<NinjaSlayerSpinMotionBlur>("SpinMotionBlur");
            if (node.GetNodeOrNull<NinjaSlayerShadowController>(NinjaSlayerVisualRig.ShadowControllerNodeName) is { } shadow)
            {
                _shadow = shadow;
                break;
            }
        }
    }

    internal static VerticalAxisSpinProjection CaptureCurrent(
        Node2D body,
        float axisCanvasX,
        Vector2 markerCanvasPosition)
    {
        CanvasItem parent = body.GetParent<CanvasItem>();
        Transform2D canvasToParent = parent.GetGlobalTransformWithCanvas().AffineInverse();
        Vector2 markerParentLocal = canvasToParent * markerCanvasPosition;
        float axisParentX = (canvasToParent * new Vector2(axisCanvasX, markerCanvasPosition.Y)).X;
        return new VerticalAxisSpinProjection(
            body,
            parent,
            body.GetGlobalTransformWithCanvas().AffineInverse() * markerCanvasPosition,
            markerParentLocal,
            axisParentX,
            body.Position,
            body.RotationDegrees,
            body.Scale);
    }

    internal static VerticalAxisSpinProjection CaptureNinjaSlayer(
        Sprite2D body,
        Node2D bodyMarker,
        Node2D axisMarker,
        float normalScaleX)
    {
        CanvasItem parent = body.GetParent<CanvasItem>();
        Transform2D canvasToParent = parent.GetGlobalTransformWithCanvas().AffineInverse();
        Vector2 bodyMarkerCanvasPosition = bodyMarker.GetGlobalTransformWithCanvas().Origin;
        Vector2 bodyMarkerParentLocal = canvasToParent * bodyMarkerCanvasPosition;
        float axisCanvasX = axisMarker.GetGlobalTransformWithCanvas().Origin.X;
        float axisParentX = (canvasToParent * new Vector2(axisCanvasX, bodyMarkerCanvasPosition.Y)).X;
        Vector2 baseScale = new(normalScaleX, body.Scale.Y);
        float axisBodyLocalY = Mathf.Abs(baseScale.Y) > 0.001f
            ? (bodyMarkerParentLocal.Y - body.Position.Y) / baseScale.Y
            : 0f;

        return new VerticalAxisSpinProjection(
            body,
            parent,
            new Vector2(NinjaSlayerVisualRig.SpinPivotDeltaX, axisBodyLocalY),
            bodyMarkerParentLocal,
            axisParentX,
            new Vector2(bodyMarkerParentLocal.X - NinjaSlayerVisualRig.SpinPivotDeltaX * baseScale.X, body.Position.Y),
            0f,
            baseScale);
    }

    internal Vector2 AxisInSprite(Sprite2D sprite) => sprite.GetGlobalTransformWithCanvas().AffineInverse()
        * (_parent.GetGlobalTransformWithCanvas() * new Vector2(_axisParentX, _markerParentLocal.Y));

    internal void ProjectVariant(Sprite2D sprite, float ratio)
    {
        Transform2D transform = sprite.Transform;
        transform.X.X *= ratio;
        transform.Y.X *= ratio;
        transform.Origin.X = VerticalSpinMath.ProjectCoordinate(_axisParentX, transform.Origin.X, ratio);
        sprite.Transform = transform;
    }

    internal void ApplyDegrees(float degrees, Func<double, double>? angleAtSecondsBefore = null)
    {
        if (!IsValid())
        {
            return;
        }

        if (_aim?.HellTornado is { Active: true } tornado && !_aim.IsFacingTurn(this))
        {
            tornado.SetActionTurn(this, degrees);
            return;
        }

        float basis = 0f;
        if (_aim != null)
            (degrees, basis, angleAtSecondsBefore) = _aim.ComposeFacingSpin(this, degrees, angleAtSecondsBefore);
        float ratio = VerticalSpinMath.GetScaleRatio(degrees);
        if (GodotObject.IsInstanceValid(_shadow)) _shadow!.SetSpin(this, Mathf.DegToRad(degrees));
        if (_body is Sprite2D sprite)
        {
            sprite.Offset = Vector2.Zero;
        }

        _body.RotationDegrees = _baseRotationDegrees;
        _body.Scale = new Vector2(_baseScale.X * ratio, _baseScale.Y);
        Vector2 projectedMarker = new(
            VerticalSpinMath.ProjectCoordinate(_axisParentX, _markerParentLocal.X, ratio),
            _markerParentLocal.Y);
        _body.Position = projectedMarker - _body.Transform.BasisXform(_markerBodyLocal);
        _blur?.Record(this, degrees, angleAtSecondsBefore, basis);
    }

    internal void Restore()
    {
        _aim?.HellTornado?.RemoveActionTurn(this);
        _blur?.Stop(this);
        if (GodotObject.IsInstanceValid(_shadow)) _shadow!.ClearSpin(this);
        if (!IsValid())
        {
            return;
        }

        _body.Position = _basePosition;
        _body.RotationDegrees = _baseRotationDegrees;
        _body.Scale = _baseScale;
        if (_body is Sprite2D sprite)
        {
            sprite.Offset = _baseOffset;
        }
    }

    private bool IsValid() =>
        GodotObject.IsInstanceValid(_body) && GodotObject.IsInstanceValid(_parent);
}
