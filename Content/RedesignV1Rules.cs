namespace NinjaSlayer.Content;

public static class RedesignV1Rules
{
    public const int StartingHp = 40;
    public const int StartingTeaEnergy = 13;
    public const int AncientTeaEnergy = 20;
    public const int RestTeaGain = 4;
    public const int ShurikenBaseDamage = 4;
    public static int ShurikenDamage(int bonus) => ShurikenBaseDamage + Math.Max(0, bonus);
}
