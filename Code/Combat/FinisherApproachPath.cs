namespace NinjaSlayer.Code.Combat;

internal enum FinisherApproachMode
{
    Stationary,
    TeleportAtStart,
    TeleportAtPeak,
    ContinuousToImpact,
    PrepositionThenLunge,
}

internal readonly record struct FinisherApproachPath(
    float OriginalX,
    float TravelStartX,
    float TravelEndX,
    float ImpactX)
{
    public static FinisherApproachPath Create(
        FinisherApproachMode mode,
        float actorX,
        float targetX,
        float targetHalfWidth,
        float approachGap,
        float authoredTravel,
        float fallbackDirection)
    {
        if (mode == FinisherApproachMode.Stationary)
        {
            return new FinisherApproachPath(actorX, actorX, actorX, actorX);
        }

        float direction = ResolveDirection(actorX, targetX, fallbackDirection);
        float halfWidth = float.IsFinite(targetHalfWidth)
            ? MathF.Max(0f, targetHalfWidth)
            : 0f;
        float gap = float.IsFinite(approachGap)
            ? MathF.Max(0f, approachGap)
            : 0f;
        float travel = float.IsFinite(authoredTravel)
            ? MathF.Max(0f, authoredTravel)
            : 0f;
        float impactX = targetX - direction * (halfWidth + gap);

        return CreateToImpact(
            mode,
            actorX,
            impactX,
            travel,
            fallbackDirection);
    }

    public static FinisherApproachPath CreateToImpact(
        FinisherApproachMode mode,
        float actorX,
        float impactX,
        float authoredTravel,
        float fallbackDirection)
    {
        if (mode == FinisherApproachMode.Stationary)
        {
            return new FinisherApproachPath(actorX, actorX, actorX, actorX);
        }

        float resolvedImpactX = float.IsFinite(impactX) ? impactX : actorX;
        float direction = ResolveDirection(actorX, resolvedImpactX, fallbackDirection);
        float travel = float.IsFinite(authoredTravel)
            ? MathF.Max(0f, authoredTravel)
            : 0f;

        if (mode == FinisherApproachMode.TeleportAtStart)
        {
            return new FinisherApproachPath(
                actorX,
                resolvedImpactX,
                resolvedImpactX,
                resolvedImpactX);
        }

        if (mode == FinisherApproachMode.PrepositionThenLunge)
        {
            float preparationX = resolvedImpactX - direction * travel;
            return new FinisherApproachPath(
                actorX,
                preparationX,
                resolvedImpactX,
                resolvedImpactX);
        }

        if (mode == FinisherApproachMode.TeleportAtPeak)
        {
            return new FinisherApproachPath(
                actorX,
                actorX,
                actorX,
                resolvedImpactX);
        }

        return new FinisherApproachPath(
            actorX,
            actorX,
            resolvedImpactX,
            resolvedImpactX);
    }

    private static float ResolveDirection(
        float actorX,
        float targetX,
        float fallbackDirection)
    {
        float delta = targetX - actorX;
        if (float.IsFinite(delta) && MathF.Abs(delta) > 0.001f)
        {
            return MathF.Sign(delta);
        }

        return float.IsFinite(fallbackDirection) && fallbackDirection < 0f
            ? -1f
            : 1f;
    }
}
