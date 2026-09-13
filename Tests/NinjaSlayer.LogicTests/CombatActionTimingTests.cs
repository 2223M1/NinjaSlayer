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
            [0.2f, 0.6f, 1f, 1.4f],
            SlowComboHits(4, CombatActionSpeed.Normal));
        AssertSequence(
            [0.2f, 0.4f],
            ProjectileHits(2, CombatActionSpeed.Normal));
    }

    [Fact]
    public void FastAndInstantModComboCadencesScaleLikeTheHost()
    {
        AssertSequence(
            [0.075f, 0.25f, 0.425f, 0.6f, 0.775f],
            SequentialDamageHits(5, CombatActionSpeed.Fast));
        AssertSequence(
            [0.1f, 0.3f, 0.5f, 0.7f],
            SlowComboHits(4, CombatActionSpeed.Fast));
        AssertSequence([0.1f, 0.2f], ProjectileHits(2, CombatActionSpeed.Fast));

        AssertSequence([0f, 0f, 0f], SequentialDamageHits(3, CombatActionSpeed.Instant));
        AssertSequence([0f, 0f, 0f], SlowComboHits(3, CombatActionSpeed.Instant));
        AssertSequence([0f, 0f], ProjectileHits(2, CombatActionSpeed.Instant));
    }

    [Theory]
    [InlineData(0.15f, 0.075f)]
    [InlineData(0.2f, 0.1f)]
    [InlineData(0.25f, 0.125f)]
    [InlineData(0.4f, 0.2f)]
    [InlineData(0.8f, 0.25f)]
    public void TriggerPreservesCallerGateAndNativeFastCap(float normal, float fast)
    {
        Assert.Equal(normal, CombatActionTiming.Trigger(CombatActionSpeed.Normal, normal));
        Assert.Equal(fast, CombatActionTiming.Trigger(CombatActionSpeed.Fast, normal));
        Assert.Equal(0f, CombatActionTiming.Trigger(CombatActionSpeed.Instant, normal));
    }

    [Theory]
    [InlineData(.075f)]
    [InlineData(.1f)]
    [InlineData(.25f)]
    [InlineData(.5f)]
    public void PresentationDoesNotChangeWithNormalOrFast(float seconds)
    {
        Assert.Equal(seconds, CombatActionTiming.Presentation(CombatActionSpeed.Normal, seconds));
        Assert.Equal(seconds, CombatActionTiming.Presentation(CombatActionSpeed.Fast, seconds));
        Assert.Equal(0f, CombatActionTiming.Presentation(CombatActionSpeed.Instant, seconds));
    }

    [Fact]
    public void FriendlyCompanionTimingIsNotHalvedByFastMode()
    {
        Assert.Equal(0.2f, CombatActionTiming.ResolveCompanion(
            CombatActionSpeed.Fast,
            CombatActionTiming.SlowAttackNormalSeconds));
        Assert.Equal(0.15f, CombatActionTiming.ResolveCompanion(
            CombatActionSpeed.Fast,
            CombatActionTiming.ConsecutiveAttackNormalSeconds));
        Assert.Equal(0.2f, CombatActionTiming.ResolveCompanion(
            CombatActionSpeed.Fast,
            CombatActionTiming.DamageRecoveryNormalSeconds));
        Assert.Equal(0f, CombatActionTiming.ResolveCompanion(
            CombatActionSpeed.Instant,
            CombatActionTiming.SlowAttackNormalSeconds));
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
        float spacing = first + Resolve(speed, CombatActionTiming.DamageRecoveryNormalSeconds, CombatActionTiming.DamageRecoveryFastSeconds);
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

    private static float Resolve(
        CombatActionSpeed speed,
        float normalSeconds,
        float fastSeconds) =>
        CombatActionTiming.Resolve(speed, normalSeconds, fastSeconds);
}
