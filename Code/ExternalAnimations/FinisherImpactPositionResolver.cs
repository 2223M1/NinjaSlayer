using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class FinisherImpactPositionResolver
{
    public static float ResolveImpactX(
        NCreature actor,
        NCreature target,
        Vector2 squashMultiplier,
        float approachGap)
    {
        try
        {
            Node2D body = target.Visuals.GetCurrentBody();
            Control bounds = target.Visuals.Bounds;
            CanvasItem? actorParent = actor.GetParent() as CanvasItem;
            if (!GodotObject.IsInstanceValid(body)
                || !GodotObject.IsInstanceValid(bounds)
                || actorParent == null
                || !GodotObject.IsInstanceValid(actorParent)
                || bounds.Size.X <= 0f
                || bounds.Size.Y <= 0f)
            {
                return ResolveFallback(actor, target, squashMultiplier, approachGap);
            }

            Transform2D bodyCanvas = body.GetGlobalTransformWithCanvas();
            Transform2D boundsCanvas = bounds.GetGlobalTransformWithCanvas();
            Transform2D bodyCanvasInverse = bodyCanvas.AffineInverse();
            var predictedBodyCanvas = new Transform2D(
                bodyCanvas.X * squashMultiplier.X,
                bodyCanvas.Y * squashMultiplier.Y,
                bodyCanvas.Origin);

            if (FinisherSquashAnchorPolicy.Resolve(
                    squashMultiplier.X,
                    squashMultiplier.Y)
                == FinisherSquashAnchorKind.BottomCenter)
            {
                Vector2 anchorCanvas = boundsCanvas
                    * new Vector2(bounds.Size.X * 0.5f, bounds.Size.Y);
                Vector2 anchorInBody = bodyCanvasInverse * anchorCanvas;
                predictedBodyCanvas.Origin = anchorCanvas
                    - predictedBodyCanvas.BasisXform(anchorInBody);
            }

            Transform2D canvasToActorParent = actorParent
                .GetGlobalTransformWithCanvas()
                .AffineInverse();
            Vector2[] corners =
            [
                Vector2.Zero,
                new Vector2(bounds.Size.X, 0f),
                bounds.Size,
                new Vector2(0f, bounds.Size.Y)
            ];
            float minimumX = float.PositiveInfinity;
            float maximumX = float.NegativeInfinity;
            foreach (Vector2 corner in corners)
            {
                Vector2 currentCanvas = boundsCanvas * corner;
                Vector2 bodyLocal = bodyCanvasInverse * currentCanvas;
                Vector2 predictedParent = canvasToActorParent
                    * (predictedBodyCanvas * bodyLocal);
                minimumX = Math.Min(minimumX, predictedParent.X);
                maximumX = Math.Max(maximumX, predictedParent.X);
            }

            if (!float.IsFinite(minimumX) || !float.IsFinite(maximumX))
            {
                return ResolveFallback(actor, target, squashMultiplier, approachGap);
            }

            float centerX = (minimumX + maximumX) * 0.5f;
            float fallbackDirection = actor.Entity.Side == CombatSide.Player ? 1f : -1f;
            float direction = ResolveDirection(actor.Position.X, centerX, fallbackDirection);
            float nearEdge = direction > 0f ? minimumX : maximumX;
            return nearEdge - direction * Math.Max(0f, approachGap);
        }
        catch
        {
            return ResolveFallback(actor, target, squashMultiplier, approachGap);
        }
    }

    private static float ResolveFallback(
        NCreature actor,
        NCreature target,
        Vector2 squashMultiplier,
        float approachGap)
    {
        float targetHalfWidth = target.Visuals.Bounds.Size.X
            * Mathf.Abs(target.Visuals.Scale.X)
            * Math.Max(0f, squashMultiplier.X)
            * 0.5f;
        float fallbackDirection = actor.Entity.Side == CombatSide.Player ? 1f : -1f;
        float direction = ResolveDirection(
            actor.Position.X,
            target.Position.X,
            fallbackDirection);
        return target.Position.X
            - direction * (targetHalfWidth + Math.Max(0f, approachGap));
    }

    private static float ResolveDirection(float actorX, float targetX, float fallbackDirection)
    {
        float delta = targetX - actorX;
        if (float.IsFinite(delta) && MathF.Abs(delta) > 0.001f)
        {
            return MathF.Sign(delta);
        }

        return fallbackDirection < 0f ? -1f : 1f;
    }
}
