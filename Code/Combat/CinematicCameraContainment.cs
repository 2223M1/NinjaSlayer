namespace NinjaSlayer.Code.Combat;

internal static class CinematicCameraContainment
{
    public static float ClampCenter(
        float desiredCenter,
        float baselineMinimum,
        float baselineMaximum,
        float baselineScale,
        float targetScale)
    {
        float midpoint = (baselineMinimum + baselineMaximum) * 0.5f;
        float halfViewport = ResolveVisibleHalfExtent(
            baselineMinimum,
            baselineMaximum,
            baselineScale,
            targetScale);
        float minimum = baselineMinimum + halfViewport;
        float maximum = baselineMaximum - halfViewport;
        return minimum <= maximum
            ? Math.Clamp(desiredCenter, minimum, maximum)
            : midpoint;
    }

    public static float ResolveVisibleHalfExtent(
        float baselineMinimum,
        float baselineMaximum,
        float baselineScale,
        float targetScale)
        => (baselineMaximum - baselineMinimum)
            * MathF.Abs(baselineScale)
            / (2f * targetScale);
}
