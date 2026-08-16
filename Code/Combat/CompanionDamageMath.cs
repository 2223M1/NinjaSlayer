namespace NinjaSlayer.Code.Combat;

internal static class CompanionDamageMath
{
    public static int ScaleForActiveRelics(int baseDamage, int activeRelicCount) =>
        baseDamage * activeRelicCount;
}
