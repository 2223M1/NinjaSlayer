using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Nodes;

namespace NinjaSlayer.Code.ExternalAnimations;

internal sealed class FinisherActorLeapPose
{
    private readonly Node2D _airborneAnchor;
    private readonly CanvasItem _airborneParent;
    private readonly Marker2D _centerMarker;
    private readonly Transform2D _baselineAnchorTransform;
    private readonly Transform2D _baselineCenterTransform;
    private readonly Vector2 _pivotInAnchor;
    private readonly Vector2 _alignedCenterPosition;
    private readonly Vector2 _returnArcOffset;
    private readonly float _tiltedRotationDegrees;
    private bool _restored;

    private FinisherActorLeapPose(
        Node2D airborneAnchor,
        CanvasItem airborneParent,
        Marker2D centerMarker,
        Transform2D baselineAnchorTransform,
        Transform2D baselineCenterTransform,
        Vector2 pivotInAnchor,
        Vector2 alignedCenterPosition,
        Vector2 returnArcOffset,
        float tiltedRotationDegrees)
    {
        _airborneAnchor = airborneAnchor;
        _airborneParent = airborneParent;
        _centerMarker = centerMarker;
        _baselineAnchorTransform = baselineAnchorTransform;
        _baselineCenterTransform = baselineCenterTransform;
        _pivotInAnchor = pivotInAnchor;
        _alignedCenterPosition = alignedCenterPosition;
        _returnArcOffset = returnArcOffset;
        _tiltedRotationDegrees = tiltedRotationDegrees;
    }

    public static FinisherActorLeapPose? TryCreate(
        Creature actor,
        NCreature actorNode,
        NCreature focusNode)
    {
        Marker2D centerMarker = actorNode.Visuals.VfxSpawnPosition;
        Marker2D targetCenterMarker = focusNode.Visuals.VfxSpawnPosition;
        Node2D? airborneAnchor = NinjaSlayerVisualRig.GetAirborneAnchor(actorNode.Visuals);
        if (airborneAnchor == null
            || airborneAnchor.GetParent() is not CanvasItem airborneParent
            || !GodotObject.IsInstanceValid(centerMarker)
            || !GodotObject.IsInstanceValid(targetCenterMarker))
        {
            return null;
        }

        Vector2 actorCenter = centerMarker.GetGlobalTransformWithCanvas().Origin;
        Vector2 targetCenter = targetCenterMarker.GetGlobalTransformWithCanvas().Origin;
        if (!FinisherLeapTrajectory.ShouldAlign(actorCenter.Y, targetCenter.Y))
        {
            return null;
        }

        JumpAnimation.StopForFinisher(actor);
        actorCenter = centerMarker.GetGlobalTransformWithCanvas().Origin;
        targetCenter = targetCenterMarker.GetGlobalTransformWithCanvas().Origin;
        if (!FinisherLeapTrajectory.ShouldAlign(actorCenter.Y, targetCenter.Y))
        {
            return null;
        }

        Transform2D baselineAnchorTransform = airborneAnchor.Transform;
        Transform2D baselineCenterTransform = centerMarker.Transform;
        try
        {
            float liftDistance = actorCenter.Y - targetCenter.Y;
            Vector2 canvasLift = new(0f, -liftDistance);
            CanvasItem centerParent = centerMarker.GetParent<CanvasItem>();
            centerMarker.Position += CanvasVectorToLocal(centerParent, canvasLift);
            airborneAnchor.Position += CanvasVectorToLocal(airborneParent, canvasLift);

            Vector2 alignedCenter = centerMarker.GetGlobalTransformWithCanvas().Origin;
            Vector2 pivotInAnchor = airborneAnchor.GetGlobalTransformWithCanvas().AffineInverse() * alignedCenter;
            float tiltedRotationDegrees = Mathf.RadToDeg(baselineAnchorTransform.Rotation)
                + FinisherLeapTrajectory.ResolveTiltDegrees(actorCenter.X, targetCenter.X);
            var pose = new FinisherActorLeapPose(
                airborneAnchor,
                airborneParent,
                centerMarker,
                baselineAnchorTransform,
                baselineCenterTransform,
                pivotInAnchor,
                centerMarker.Position,
                CanvasVectorToLocal(
                    centerParent,
                    new Vector2(0f, -FinisherLeapTrajectory.ResolveArcHeight(liftDistance))),
                tiltedRotationDegrees);
            pose.ApplyTransform(tiltedRotationDegrees);
            return pose;
        }
        catch
        {
            if (GodotObject.IsInstanceValid(airborneAnchor))
            {
                airborneAnchor.Transform = baselineAnchorTransform;
            }

            if (GodotObject.IsInstanceValid(centerMarker))
            {
                centerMarker.Transform = baselineCenterTransform;
            }

            throw;
        }
    }

    public void ApplyReturn(float progress)
    {
        if (_restored
            || !GodotObject.IsInstanceValid(_airborneAnchor)
            || !GodotObject.IsInstanceValid(_airborneParent)
            || !GodotObject.IsInstanceValid(_centerMarker))
        {
            return;
        }

        float p = Mathf.Clamp(progress, 0f, 1f);
        _centerMarker.Position = _alignedCenterPosition.Lerp(_baselineCenterTransform.Origin, p)
            + _returnArcOffset * FinisherLeapTrajectory.ResolveReturnArcFactor(p);
        ApplyTransform(Mathf.Lerp(
            _tiltedRotationDegrees,
            Mathf.RadToDeg(_baselineAnchorTransform.Rotation),
            p));
    }

    public void Restore()
    {
        if (_restored)
        {
            return;
        }

        _restored = true;
        if (GodotObject.IsInstanceValid(_airborneAnchor))
        {
            _airborneAnchor.Transform = _baselineAnchorTransform;
        }

        if (GodotObject.IsInstanceValid(_centerMarker))
        {
            _centerMarker.Transform = _baselineCenterTransform;
        }
    }

    private void ApplyTransform(float rotationDegrees)
    {
        Vector2 pivotInParent = _airborneParent.GetGlobalTransformWithCanvas().AffineInverse()
            * _centerMarker.GetGlobalTransformWithCanvas().Origin;
        (float x, float y) = FixedPivotMath.ResolveBodyPosition(
            pivotInParent.X,
            pivotInParent.Y,
            _pivotInAnchor.X,
            _pivotInAnchor.Y,
            rotationDegrees,
            _baselineAnchorTransform.Scale.X,
            _baselineAnchorTransform.Scale.Y);
        _airborneAnchor.Position = new Vector2(x, y);
        _airborneAnchor.RotationDegrees = rotationDegrees;
        _airborneAnchor.Scale = _baselineAnchorTransform.Scale;
    }

    private static Vector2 CanvasVectorToLocal(CanvasItem parent, Vector2 canvasVector)
    {
        Transform2D canvasToParent = parent.GetGlobalTransformWithCanvas().AffineInverse();
        return canvasToParent.BasisXform(canvasVector);
    }
}
