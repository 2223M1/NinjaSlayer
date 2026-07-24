namespace NinjaSlayer.Code.ExternalAnimations;

internal static class ArchitectRagdollMath
{
    public static float EaseOutCubic(float progress)
    {
        float clamped = Math.Clamp(progress, 0f, 1f);
        float inverse = 1f - clamped;
        return 1f - inverse * inverse * inverse;
    }

    public static float ParabolicOffset(float progress, float lift, float drop)
    {
        float eased = EaseOutCubic(progress);
        return drop * eased - 4f * lift * eased * (1f - eased);
    }

    public static float RotatedScaledY(
        float x,
        float y,
        float scaleX,
        float scaleY,
        float rotationDegrees)
    {
        float radians = rotationDegrees * MathF.PI / 180f;
        return x * scaleX * MathF.Sin(radians)
            + y * scaleY * MathF.Cos(radians);
    }
}
