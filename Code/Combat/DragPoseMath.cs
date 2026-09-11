using System.Numerics;

namespace NinjaSlayer.Code.Combat;

internal static class DragPoseMath
{
    internal const float TurnSeconds = .15f;

    internal static float Smooth(float value)
    {
        float p = Math.Clamp(value, 0f, 1f);
        return p * p * (3f - 2f * p);
    }

    internal static float TurnAngle(float from, float to, float elapsed, float duration) =>
        duration <= 0f ? to : from + (to - from) * Smooth(elapsed / duration);

    internal static float LimitedAngle(ReadOnlySpan<Vector2> contour, Vector2 core, Vector2 pointer,
        ReadOnlySpan<Vector2> targets, float line, float reference, float previous)
    {
        float minimum = 0f, maximum = 0f;
        foreach (Vector2 target in targets)
        {
            float angle = GroundedPoseMath.AimAngle(contour, core, target, line, reference, 0f);
            minimum = Math.Min(minimum, angle);
            maximum = Math.Max(maximum, angle);
        }
        if (minimum == maximum) return minimum;
        return Math.Clamp(GroundedPoseMath.AimAngle(contour, core, pointer, line, reference, previous), minimum, maximum);
    }
}

internal readonly record struct TornadoChargeProfile(
    float Back, float Seconds, float ScaleX, float ScaleY, float TrembleX, float TrembleY, float Hertz)
{
    internal static TornadoChargeProfile Default => new(32f, .25f, 1.04f, .94f, 1.3f, .008f, 10f);

    internal (float Back, Vector2 Scale) Sample(float elapsed)
    {
        float p = DragPoseMath.Smooth(elapsed / Seconds);
        float tremble = DragPoseMath.Smooth((elapsed / Seconds - .5f) * 2f);
        float wave = MathF.Sin(MathF.Tau * Hertz * elapsed);
        return (Back * p + TrembleX * tremble * wave,
            new(1f + (ScaleX - 1f) * p, 1f + (ScaleY - 1f) * p + TrembleY * tremble * wave));
    }
}
