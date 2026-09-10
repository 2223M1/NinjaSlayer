using System.Numerics;

namespace NinjaSlayer.Code.Combat;

internal static class GroundedPoseMath
{
    internal static float WrapAngle(float radians) => MathF.Atan2(MathF.Sin(radians), MathF.Cos(radians));

    internal static Vector2 Rotate(Vector2 point, float radians)
    {
        float c = MathF.Cos(radians), s = MathF.Sin(radians);
        return new(point.X * c - point.Y * s, point.X * s + point.Y * c);
    }

    internal static float SupportY(ReadOnlySpan<Vector2> offsets, float rotation)
    {
        float maximum = float.NegativeInfinity;
        foreach (Vector2 point in offsets)
            maximum = Math.Max(maximum, Rotate(point, rotation).Y);
        return maximum;
    }

    internal static float CoreY(ReadOnlySpan<Vector2> offsets, float rotation, float supportLine) =>
        supportLine - SupportY(offsets, rotation);

    internal static float AimAngle(
        ReadOnlySpan<Vector2> offsets,
        Vector2 core,
        Vector2 target,
        float supportLine,
        float referenceAngle,
        float previousAngle)
    {
        // The vertical grounding correction moves the pivot. Solve against that corrected
        // pivot rather than the previous frame, which otherwise feeds back into the aim.
        float angle = previousAngle;
        for (int i = 0; i < 64; i++)
        {
            Vector2 direction = target - new Vector2(core.X, CoreY(offsets, angle, supportLine));
            if (direction.LengthSquared() < 0.0001f)
                return angle;
            float desired = WrapAngle(MathF.Atan2(direction.Y, direction.X) - referenceAngle);
            float change = WrapAngle(desired - angle);
            if (MathF.Abs(change) < 0.00001f)
                return desired;
            angle = WrapAngle(angle + change * 0.5f);
        }
        return angle;
    }

    internal static float ClampDescent(float verticalOffset, float airborneHeight) =>
        Math.Min(verticalOffset, Math.Max(0f, airborneHeight));
}
