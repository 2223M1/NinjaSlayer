using Godot;

namespace NinjaSlayer.Code.ExternalAnimations;

internal readonly record struct FixedPivotTransform(
    Node2D Body,
    CanvasItem Parent,
    Vector2 PivotBodyLocal,
    Vector2 PivotParentLocal)
{
    internal static bool TryCapture(
        Node2D body,
        Node2D pivotMarker,
        out FixedPivotTransform transform)
    {
        transform = default;
        if (!GodotObject.IsInstanceValid(body)
            || !GodotObject.IsInstanceValid(pivotMarker)
            || body.GetParent() is not CanvasItem parent
            || !GodotObject.IsInstanceValid(parent))
        {
            return false;
        }

        Vector2 pivotCanvas = pivotMarker.GetGlobalTransformWithCanvas().Origin;
        transform = new FixedPivotTransform(
            body,
            parent,
            body.GetGlobalTransformWithCanvas().AffineInverse() * pivotCanvas,
            parent.GetGlobalTransformWithCanvas().AffineInverse() * pivotCanvas);
        return true;
    }

    internal void Apply(float rotationDegrees, Vector2 scale)
    {
        if (!GodotObject.IsInstanceValid(Body) || !GodotObject.IsInstanceValid(Parent))
        {
            return;
        }

        Body.RotationDegrees = rotationDegrees;
        Body.Scale = scale;
        (float x, float y) = FixedPivotMath.ResolveBodyPosition(
            PivotParentLocal.X,
            PivotParentLocal.Y,
            PivotBodyLocal.X,
            PivotBodyLocal.Y,
            rotationDegrees,
            scale.X,
            scale.Y);
        Body.Position = new Vector2(x, y);
    }
}
