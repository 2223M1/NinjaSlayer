namespace NinjaSlayer.Code.Combat;

internal readonly record struct ExtendedHealthBarLayout(
    float BarLeft,
    float BarWidth,
    float BarRight,
    float BlockLeft);

internal readonly record struct EmbeddedHealthBarSegment(float OffsetLeft, float OffsetRight);

internal static class ExtendedHealthBarLayoutCalculator
{
    public static int GetEmbeddedNarakuLife(int currentHp, int maxHp, int narakuLife) =>
        currentHp > 0
        && maxHp > 0
        && narakuLife > 0
        && (long)currentHp + narakuLife <= maxHp
            ? narakuLife
            : 0;

    public static EmbeddedHealthBarSegment? CalculateEmbeddedNarakuLife(
        int currentHp,
        int maxHp,
        int narakuLife,
        float maxWidth,
        float patchMarginLeft)
    {
        int embeddedLife = GetEmbeddedNarakuLife(currentHp, maxHp, narakuLife);
        if (embeddedLife == 0 || maxWidth <= 0f)
        {
            return null;
        }

        float mainWidth = Math.Max((float)currentHp / maxHp * maxWidth, 12f);
        float lifeWidth = (float)embeddedLife / maxHp * maxWidth;
        return new EmbeddedHealthBarSegment(
            Math.Max(0f, mainWidth - patchMarginLeft),
            mainWidth + lifeWidth - maxWidth);
    }

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
