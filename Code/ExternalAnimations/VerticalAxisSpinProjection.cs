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
        _parent = parent;
        _markerBodyLocal = markerBodyLocal;
        _markerParentLocal = markerParentLocal;
        _axisParentX = axisParentX;
        _basePosition = basePosition;
        _baseRotationDegrees = baseRotationDegrees;
        _baseScale = baseScale;
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
            body.Position,
            0f,
            baseScale);
    }

    internal void ApplyDegrees(float degrees)
    {
        if (!IsValid())
        {
            return;
        }

        float ratio = VerticalSpinMath.GetScaleRatio(degrees);
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
    }

    internal void Restore()
    {
        if (!IsValid())
        {
            return;
        }

        _body.Position = _basePosition;
        _body.RotationDegrees = _baseRotationDegrees;
        _body.Scale = _baseScale;
        if (_body is Sprite2D sprite)
        {
            sprite.Offset = Vector2.Zero;
        }
    }

    private bool IsValid() =>
        GodotObject.IsInstanceValid(_body) && GodotObject.IsInstanceValid(_parent);
}
