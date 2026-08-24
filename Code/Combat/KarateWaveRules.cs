namespace NinjaSlayer.Code.Combat;

public readonly record struct KarateWaveResolution(
    int BonusDamagePerTarget,
    int RemainingStacks,
    int EligibleTargetCount)
{
    public bool Triggered => BonusDamagePerTarget > 0 && EligibleTargetCount > 0;
}

public static class KarateWaveRules
{
    public static bool IsEligibleHit(int totalDamage, bool isOpposingSide, bool canReceiveBonus) =>
        totalDamage > 0 && isOpposingSide && canReceiveBonus;

    public static KarateWaveResolution Resolve(int stacks, bool isPoweredAttack, int eligibleTargetCount)
    {
        if (stacks <= 0 || !isPoweredAttack || eligibleTargetCount <= 0)
        {
            return new KarateWaveResolution(0, Math.Max(0, stacks), 0);
        }

        return new KarateWaveResolution(stacks, stacks - 1, eligibleTargetCount);
    }
}
