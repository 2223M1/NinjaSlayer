namespace NinjaSlayer.Code.ExternalAnimations;

internal static class VerticalSpinMath
{
    internal const float MinScaleRatio = 0.18f;

    internal static float GetScaleRatio(float degrees)
    {
        float ratio = MathF.Cos(degrees * MathF.PI / 180f);
        if (MathF.Abs(ratio) >= MinScaleRatio)
        {
            return ratio;
        }

        return ratio < 0f ? -MinScaleRatio : MinScaleRatio;
    }

    internal static float ProjectCoordinate(float axis, float coordinate, float scaleRatio) =>
        axis + (coordinate - axis) * scaleRatio;
}
