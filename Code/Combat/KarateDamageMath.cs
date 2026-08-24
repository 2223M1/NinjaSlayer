namespace NinjaSlayer.Code.Combat;

public static class KarateDamageMath
{
    public static int CumulativeDamage(int stack, int hits)
    {
        if (stack <= 0 || hits <= 0)
        {
            return 0;
        }

        int triggers = Math.Min(stack, hits);
        return triggers * (2 * stack - triggers + 1) / 2;
    }

    public static int ForecastDamage(int stack, int hits, bool isPreviewTarget) =>
        isPreviewTarget ? CumulativeDamage(stack, hits) : Math.Max(0, stack);

    public static int HpPreviewDamage(int stack, int hits, bool isPreviewTarget) =>
        isPreviewTarget ? CumulativeDamage(stack, hits) : 0;
}
