using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Code.Nodes;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class FinisherImpactPositionResolver
{
    public static float ResolveImpactX(
        NCreature actor,
        NCreature target,
        float approachGap)
    {
        // Only Sawatari has a separately assembled, changing weapon silhouette.
        Transform2D canvasToParent = actor.GetParent<CanvasItem>().GetGlobalTransformWithCanvas().AffineInverse();
        float ContactX(float nearEdge, float direction)
        {
            float reach = 0f;
            if (SawatariWeaponVisuals.Get(actor.Entity) is { } actorWeapons)
            {
                Rect2 actorBounds = actorWeapons.GetCombatBounds(canvasToParent);
                float front = direction > 0f ? actorBounds.End.X : actorBounds.Position.X;
                reach = (front - actor.Position.X) * direction;
            }
            return nearEdge - direction * (Math.Max(0f, approachGap) + reach);
        }

        float Fallback() => ContactX(ResolveFallback(actor, target, 0f),
            ResolveDirection(actor.Position.X, target.Position.X, actor.Entity.Side == CombatSide.Player ? 1f : -1f));

        try
        {
            Control bounds = target.Visuals.Bounds;
            CanvasItem? actorParent = actor.GetParent() as CanvasItem;
            if (!GodotObject.IsInstanceValid(bounds)
                || actorParent == null
                || !GodotObject.IsInstanceValid(actorParent)
                || bounds.Size.X <= 0f
                || bounds.Size.Y <= 0f)
            {
                return Fallback();
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
                Vector2 predictedParent = canvasToActorParent
                    * (bounds.GetGlobalTransformWithCanvas() * corner);
                minimumX = Math.Min(minimumX, predictedParent.X);
                maximumX = Math.Max(maximumX, predictedParent.X);
            }

            if (!float.IsFinite(minimumX) || !float.IsFinite(maximumX))
            {
                return Fallback();
            }

            float centerX = (minimumX + maximumX) * 0.5f;
            float fallbackDirection = actor.Entity.Side == CombatSide.Player ? 1f : -1f;
            float direction = ResolveDirection(actor.Position.X, centerX, fallbackDirection);
            float nearEdge = direction > 0f ? minimumX : maximumX;
            return ContactX(nearEdge, direction);
        }
        catch
        {
            return Fallback();
        }
    }

    private static float ResolveFallback(
        NCreature actor,
        NCreature target,
        float approachGap)
    {
        float targetHalfWidth = target.Visuals.Bounds.Size.X
            * Mathf.Abs(target.Visuals.Scale.X)
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
