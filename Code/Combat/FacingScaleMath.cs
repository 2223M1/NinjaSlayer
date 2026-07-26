namespace NinjaSlayer.Code.Combat;

internal static class FacingScaleMath
{
    private const float MinimumMagnitude = 0.001f;

    public static float WithFacing(float scaleX, bool faceLeft, float fallbackMagnitude = 1f)
    {
        float magnitude = MathF.Abs(scaleX);
        if (!float.IsFinite(magnitude) || magnitude <= MinimumMagnitude)
        {
            magnitude = MathF.Max(MathF.Abs(fallbackMagnitude), MinimumMagnitude);
        }

        return faceLeft ? -magnitude : magnitude;
    }

    public static bool IsFacingLeft(float scaleX, bool fallback = false) =>
        float.IsFinite(scaleX) && MathF.Abs(scaleX) > MinimumMagnitude
            ? scaleX < 0f
            : fallback;
}
