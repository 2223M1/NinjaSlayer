namespace NinjaSlayer.Code.Combat;

public readonly record struct KarateWaveResolution(
    int BonusDamagePerTarget,
    int RemainingStacks,
    int EligibleTargetCount,
    int RemainingNarakuLife = 0)
{
    public bool Triggered => BonusDamagePerTarget > 0 && EligibleTargetCount > 0;
}

public static class KarateWaveRules
{
    public static bool IsEligibleHit(int totalDamage, bool isOpposingSide) =>
        totalDamage > 0 && isOpposingSide;

    public static KarateWaveResolution Resolve(int stacks, bool isPoweredAttack, int eligibleTargetCount,
        int narakuLife = 0, int narakuMultiplier = 0)
    {
        int damage = stacks + narakuLife * narakuMultiplier;
        if (damage <= 0 || !isPoweredAttack || eligibleTargetCount <= 0)
        {
            return new KarateWaveResolution(0, Math.Max(0, stacks), 0, narakuLife);
        }

        return narakuLife > 0 && narakuMultiplier > 0
            ? new KarateWaveResolution(damage, stacks, eligibleTargetCount, narakuLife - 1)
            : new KarateWaveResolution(damage, stacks - 1, eligibleTargetCount, narakuLife);
    }
}
