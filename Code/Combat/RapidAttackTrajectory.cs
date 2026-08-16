namespace NinjaSlayer.Code.Combat;

internal static class RapidAttackTrajectory
{
    public const float ReturnSeconds = 0.2f;

    public static float GetContinuationOffset(
        float progress,
        float currentOffset,
        float retreatOffset,
        float peakOffset,
        Func<float, float> outboundCurve)
    {
        float p = Math.Clamp(progress, 0f, 1f);
        if (p < 0.5f)
        {
            return Lerp(currentOffset, retreatOffset, SmoothStep(p * 2f));
        }

        return Lerp(retreatOffset, peakOffset, outboundCurve((p - 0.5f) * 2f));
    }

    private static float SmoothStep(float progress) =>
        progress * progress * (3f - 2f * progress);

    private static float Lerp(float from, float to, float progress) =>
        from + (to - from) * progress;
}
