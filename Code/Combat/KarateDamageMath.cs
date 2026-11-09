namespace NinjaSlayer.Code.Combat;

public static class KarateDamageMath
{
    public static int CumulativeDamage(int stack, int hits, int narakuLife = 0, int narakuMultiplier = 0)
    {
        if (hits <= 0)
        {
            return 0;
        }

        int lifeTriggers = narakuMultiplier > 0 ? Math.Min(narakuLife, hits) : 0;
        int lifeDamage = lifeTriggers * stack
            + narakuMultiplier * lifeTriggers * (2 * narakuLife - lifeTriggers + 1) / 2;
        int triggers = Math.Min(Math.Max(0, stack), hits - lifeTriggers);
        return lifeDamage + triggers * (2 * stack - triggers + 1) / 2;
    }

    public static int ForecastDamage(int stack, int hits, bool isPreviewTarget, int narakuLife = 0, int narakuMultiplier = 0) =>
        CumulativeDamage(stack, isPreviewTarget ? hits : 1, narakuLife, narakuMultiplier);

    public static int HpPreviewDamage(int stack, int hits, bool isPreviewTarget, int narakuLife = 0, int narakuMultiplier = 0) =>
        isPreviewTarget ? CumulativeDamage(stack, hits, narakuLife, narakuMultiplier) : 0;
}
