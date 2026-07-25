namespace NinjaSlayer.Code.Combat;

public static class YamotoKokiDamageMath
{
    public static int ScaleForParty(int baseDamage, int playerCount) =>
        checked(baseDamage * Math.Max(1, playerCount));
}
