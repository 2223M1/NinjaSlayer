namespace NinjaSlayer.Code.Combat;

public static class YamotoKokiDamageMath
{
    public static int ScaleForActiveRelics(int baseDamage, int activeRelicCount) =>
        checked(baseDamage * Math.Max(0, activeRelicCount));
}
