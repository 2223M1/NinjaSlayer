namespace NinjaSlayer.Code.Combat;

internal static class FinisherActionTrajectory
{
    public const float SlowTravelPixels = 120f;
    public const float SlowTravelSeconds = 0.25f;

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
