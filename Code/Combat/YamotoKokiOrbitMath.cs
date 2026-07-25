namespace NinjaSlayer.Code.Combat;

internal static class YamotoKokiOrbitMath
{
    internal const int BombsPerArc = 6;
    private const float ArcStartDegrees = -150f;
    private const float ArcEndDegrees = -25f;
    private const float BaseRadius = 190f;
    private const float LayerRadiusStep = 70f;

    public static (float X, float Y) GetOffset(int bombCount, int index)
    {
        if (bombCount <= 0 || index < 0 || index >= bombCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        int layer = index / BombsPerArc;
        int layerIndex = index % BombsPerArc;
        int layerCount = Math.Min(BombsPerArc, bombCount - layer * BombsPerArc);
        float progress = layerCount == 1 ? 0.5f : (float)layerIndex / (layerCount - 1);
        float angleDegrees = ArcStartDegrees + (ArcEndDegrees - ArcStartDegrees) * progress;
        float angle = angleDegrees * MathF.PI / 180f;
        float radius = BaseRadius + layer * LayerRadiusStep;
        return (-MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
    }
}
