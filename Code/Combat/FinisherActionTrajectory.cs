namespace NinjaSlayer.Code.Combat;

internal static class FinisherActionTrajectory
{
    public const float SlowTravelPixels = 120f;
    public const float SlowTravelSeconds = CombatActionTiming.SlowAttackNormalSeconds;

    public static float FastProgress(float progress)
    {
        float p = Math.Clamp(progress, 0f, 1f);
        return p * p * (3f - 2f * p);
    }

    public static float SlowProgress(float progress) =>
        MathF.Pow(Math.Clamp(progress, 0f, 1f), 10f);

    public static float ResolveIaiStartX(float actorX, float impactX, float fallbackDirection)
    {
        float delta = impactX - actorX;
        float direction = float.IsFinite(delta) && MathF.Abs(delta) > 0.001f
            ? MathF.Sign(delta)
            : fallbackDirection < 0f ? -1f : 1f;
        return impactX - direction * SlowTravelPixels;
    }
}

internal static class FinisherLeapTrajectory
{
    public const float HeightTolerance = 0.5f;
    public const float TiltDegrees = 6f;
    public const float MaximumArcHeight = 24f;

    public static bool ShouldAlign(float actorCenterY, float targetCenterY) =>
        float.IsFinite(actorCenterY)
        && float.IsFinite(targetCenterY)
        && actorCenterY > targetCenterY + HeightTolerance;

    public static float ResolveTiltDegrees(float actorCenterX, float targetCenterX) =>
        targetCenterX >= actorCenterX ? TiltDegrees : -TiltDegrees;

    public static float ResolveArcHeight(float liftDistance) =>
        MathF.Min(MaximumArcHeight, MathF.Max(0f, liftDistance) * 0.25f);

    public static float ResolveReturnArcFactor(float progress)
    {
        float p = Math.Clamp(progress, 0f, 1f);
        return 4f * p * (1f - p);
    }

}
