namespace NinjaSlayer.Code.Combat;

internal static class FinisherActionTrajectory
{
    public const float FastTravelPixels = 90f;
    public const float FastTravelSeconds = 0.15f;
    public const float SlowTravelPixels = 120f;
    public const float SlowTravelSeconds = 0.25f;

    public static float FastProgress(float progress)
    {
        float p = Math.Clamp(progress, 0f, 1f);
        return p * p * (3f - 2f * p);
    }

    public static float SlowProgress(float progress) =>
        MathF.Pow(Math.Clamp(progress, 0f, 1f), 10f);
}
