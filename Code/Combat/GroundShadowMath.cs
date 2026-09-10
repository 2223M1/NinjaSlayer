using System.Numerics;

namespace NinjaSlayer.Code.Combat;

internal enum ShadowActionKind { Attack, SlowAttack, Cast, Hurt }

internal readonly record struct ShadowSupport(float Left, float Right, float Bottom, float Height)
{
    internal float Center => (Left + Right) * 0.5f;
    internal float Width => Math.Max(1f, Right - Left);
}

internal static class GroundShadowMath
{
    // STS2 0.111.0 Ironclad shadow-bone keys, relative to the setup pose.
    internal static Vector2 ActionScale(ShadowActionKind kind) => kind switch
    {
        ShadowActionKind.Attack => new(1.2475132f, 0.7855182f),
        ShadowActionKind.SlowAttack => new(1.0878955f, 1.0878956f),
        ShadowActionKind.Cast => new(0.9755064f, 1f),
        ShadowActionKind.Hurt => new(0.9222600f, 1f),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    internal static ShadowSupport Measure(ReadOnlySpan<Vector2> points)
    {
        if (points.Length < 3) throw new ArgumentException("A shadow requires an authored body contour.", nameof(points));
        float top = float.PositiveInfinity, bottom = float.NegativeInfinity;
        foreach (Vector2 point in points) { top = Math.Min(top, point.Y); bottom = Math.Max(bottom, point.Y); }
        float line = bottom - Math.Max(1f, (bottom - top) * 0.12f);
        float left = float.PositiveInfinity, right = float.NegativeInfinity;
        for (int i = 0; i < points.Length; i++)
        {
            Vector2 a = points[i], b = points[(i + 1) % points.Length];
            if (a.Y >= line) { left = Math.Min(left, a.X); right = Math.Max(right, a.X); }
            if ((a.Y < line) != (b.Y < line))
            {
                float x = a.X + (b.X - a.X) * (line - a.Y) / (b.Y - a.Y);
                left = Math.Min(left, x); right = Math.Max(right, x);
            }
        }
        return new(left, right, bottom, Math.Max(1f, bottom - top));
    }

    internal static (float Width, float Depth, float Alpha) Airborne(float altitude, float bodyHeight)
    {
        // Preserve a readable ground footprint without a hard height cutoff.
        float height = Math.Max(0f, altitude) / Math.Max(1f, bodyHeight);
        float spread = 1f - MathF.Exp(-height);
        return (1f - 0.22f * spread, 1f - 0.15f * spread, MathF.Exp(-1.35f * height));
    }

    internal static float SpinWidth(float radians)
    {
        float c = MathF.Cos(radians), s = MathF.Sin(radians);
        return MathF.Sqrt(c * c + 0.55f * 0.55f * s * s);
    }
}
