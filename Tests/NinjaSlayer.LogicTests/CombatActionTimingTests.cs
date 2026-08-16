using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.LogicTests;

public sealed class CombatActionTimingTests
{
    [Fact]
    public void ResolvesIroncladActionAndRecoveryTiming()
    {
        AssertTiming(CombatActionSpeed.Normal, 0.15f, 0.2f, 0.25f, 0.2f);
        AssertTiming(CombatActionSpeed.Fast, 0.075f, 0.1f, 0.125f, 0.1f);
        AssertTiming(CombatActionSpeed.Instant, 0f, 0f, 0f, 0f);
    }

    [Fact]
    public void NormalModComboCadencesMatchTheirHostReferences()
    {
        AssertSequence(
            [0.15f, 0.5f, 0.85f, 1.2f, 1.55f],
            SequentialDamageHits(5, CombatActionSpeed.Normal));
        AssertSequence(
            [0.2f, 0.35f, 0.5f, 0.65f],
            SlowComboHits(4, CombatActionSpeed.Normal));
        AssertSequence(
            [0.2f, 0.4f],
            ProjectileHits(2, CombatActionSpeed.Normal));
        Assert.Equal(0.4f, KokiIaiDuration(CombatActionSpeed.Normal));
    }

    [Fact]
    public void FastAndInstantModComboCadencesScaleLikeTheHost()
    {
        AssertSequence(
            [0.075f, 0.25f, 0.425f, 0.6f, 0.775f],
            SequentialDamageHits(5, CombatActionSpeed.Fast));
        AssertSequence(
            [0.1f, 0.175f, 0.25f, 0.325f],
            SlowComboHits(4, CombatActionSpeed.Fast));
        AssertSequence([0.1f, 0.2f], ProjectileHits(2, CombatActionSpeed.Fast));
        Assert.Equal(0.2f, KokiIaiDuration(CombatActionSpeed.Fast));

        AssertSequence([0f, 0f, 0f], SequentialDamageHits(3, CombatActionSpeed.Instant));
        AssertSequence([0f, 0f, 0f], SlowComboHits(3, CombatActionSpeed.Instant));
        AssertSequence([0f, 0f], ProjectileHits(2, CombatActionSpeed.Instant));
        Assert.Equal(0f, KokiIaiDuration(CombatActionSpeed.Instant));
    }

    private static void AssertTiming(
        CombatActionSpeed speed,
        float attack,
        float slowAttack,
        float cast,
        float damageRecovery)
    {
        Assert.Equal(attack, Resolve(speed, CombatActionTiming.AttackNormalSeconds, CombatActionTiming.AttackFastSeconds));
        Assert.Equal(slowAttack, Resolve(speed, CombatActionTiming.SlowAttackNormalSeconds, CombatActionTiming.SlowAttackFastSeconds));
        Assert.Equal(cast, Resolve(speed, CombatActionTiming.CastNormalSeconds, CombatActionTiming.CastFastSeconds));
        Assert.Equal(damageRecovery, Resolve(speed, CombatActionTiming.DamageRecoveryNormalSeconds, CombatActionTiming.DamageRecoveryFastSeconds));
    }

    private static void AssertSequence(float[] expected, float[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int index = 0; index < expected.Length; index++)
        {
            Assert.Equal(expected[index], actual[index], 5);
        }
    }

    private static float[] SequentialDamageHits(int count, CombatActionSpeed speed)
    {
        float attack = Resolve(speed, CombatActionTiming.AttackNormalSeconds, CombatActionTiming.AttackFastSeconds);
        float recovery = Resolve(speed, CombatActionTiming.DamageRecoveryNormalSeconds, CombatActionTiming.DamageRecoveryFastSeconds);
        return Enumerable.Range(0, count)
            .Select(index => attack + index * (attack + recovery))
            .ToArray();
    }

    private static float[] SlowComboHits(int count, CombatActionSpeed speed)
    {
        float first = Resolve(speed, CombatActionTiming.SlowAttackNormalSeconds, CombatActionTiming.SlowAttackFastSeconds);
        float spacing = Resolve(speed, CombatActionTiming.ConsecutiveAttackNormalSeconds, CombatActionTiming.ConsecutiveAttackFastSeconds);
        return Enumerable.Range(0, count)
            .Select(index => first + index * spacing)
            .ToArray();
    }

    private static float[] ProjectileHits(int count, CombatActionSpeed speed)
    {
        float duration = Resolve(speed, CombatActionTiming.SlowAttackNormalSeconds, CombatActionTiming.SlowAttackFastSeconds);
        return Enumerable.Range(1, count)
            .Select(index => index * duration)
            .ToArray();
    }

    private static float KokiIaiDuration(CombatActionSpeed speed)
    {
        float approach = Resolve(speed, CombatActionTiming.SlowAttackNormalSeconds, CombatActionTiming.SlowAttackFastSeconds);
        float recovery = Resolve(speed, CombatActionTiming.DamageRecoveryNormalSeconds, CombatActionTiming.DamageRecoveryFastSeconds);
        return approach + recovery;
    }

    private static float Resolve(
        CombatActionSpeed speed,
        float normalSeconds,
        float fastSeconds) =>
        CombatActionTiming.Resolve(speed, normalSeconds, fastSeconds);
}
