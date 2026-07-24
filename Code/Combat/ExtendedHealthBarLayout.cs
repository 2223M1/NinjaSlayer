namespace NinjaSlayer.Code.Combat;

internal readonly record struct ExtendedHealthBarLayout(
    float BarLeft,
    float BarWidth,
    float BarRight,
    float BlockLeft);

internal static class ExtendedHealthBarLayoutCalculator
{
    public static ExtendedHealthBarLayout Calculate(
        float creatureBoundsLeft,
        float creatureBoundsWidth,
        float vanillaPadding,
        float widthMultiplier,
        float blockWidth)
    {
        float baseWidth = Math.Max(0f, creatureBoundsWidth + vanillaPadding);
        float multiplier = float.IsFinite(widthMultiplier)
            ? Math.Max(1f, widthMultiplier)
            : 1f;
        float barLeft = creatureBoundsLeft - vanillaPadding * 0.5f;
        float barWidth = baseWidth * multiplier;
        return new ExtendedHealthBarLayout(
            barLeft,
            barWidth,
            barLeft + barWidth,
            creatureBoundsLeft - Math.Max(0f, blockWidth) * 0.5f);
    }
}
