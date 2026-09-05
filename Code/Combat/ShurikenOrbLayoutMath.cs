namespace NinjaSlayer.Code.Combat;

internal readonly record struct ShurikenOrbSlotPosition(float X, float Y);

internal static class ShurikenOrbLayoutMath
{
    private const float ArcDegrees = 125f;
    private const float AngleOffsetDegrees = -25f;
    private const float MinRadius = 225f;
    private const float MaxRadius = 300f;

    internal static ShurikenOrbSlotPosition GetStandardPosition(
        int capacity,
        int index,
        bool isLocal)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, capacity);

        float step = capacity == 1 ? 0f : ArcDegrees / (capacity - 1);
        float angle = MathF.PI / 180f * (AngleOffsetDegrees - ArcDegrees + index * step);
        float radius = MinRadius + (MaxRadius - MinRadius) * ((capacity - 3f) / 7f);
        if (!isLocal)
        {
            radius *= 0.75f;
        }

        return new ShurikenOrbSlotPosition(-MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
    }
}
