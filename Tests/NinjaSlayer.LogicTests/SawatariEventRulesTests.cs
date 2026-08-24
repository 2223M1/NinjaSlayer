using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.LogicTests;

public sealed class SawatariEventRulesTests
{
    [Fact]
    public void CombatValuesMatchTheCoopAndDuelSequence()
    {
        Assert.Equal(2, SawatariEventRules.AttackDamage);
        Assert.Equal(4, SawatariEventRules.AttackHits);
        Assert.Equal(2, SawatariEventRules.DuelStrength);
    }

    [Theory]
    [InlineData(250f, 1f, 480f)]
    [InlineData(800f, 1f, 550f)]
    public void IntermissionUsesVanillaSingleEnemyPosition(
        float boundsWidth,
        float encounterScaling,
        float expectedX) =>
        Assert.Equal(
            expectedX,
            SawatariEventRules.ResolveSingleEnemyX(boundsWidth, encounterScaling));

    [Theory]
    [InlineData(0.40f, 8, 0.20f, 0.25f)]
    [InlineData(0.40f, 2, 0.10f, 1f)]
    [InlineData(0f, 8, 0.20f, 0f)]
    [InlineData(0.40f, 0, 0.20f, 0f)]
    [InlineData(0.40f, 8, 0f, 0f)]
    public void ReplacementChanceMatchesOneOrdinaryEventShare(
        float eventOdds,
        int validEventCount,
        float monsterOdds,
        float expected) =>
        Assert.Equal(
            expected,
            SawatariEventRules.ResolveMonsterReplacementChance(
                eventOdds,
                validEventCount,
                monsterOdds),
            precision: 5);

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void IntermissionWaitsForEveryEnemyToDie(
        bool defeatedCreatureIsEnemy,
        bool hasOtherLivingEnemy,
        bool expected) =>
        Assert.Equal(
            expected,
            SawatariEventRules.ShouldBeginIntermission(
                defeatedCreatureIsEnemy,
                hasOtherLivingEnemy));

    [Fact]
    public void PhaseTransitionsAreIdempotent()
    {
        var phases = new SawatariEventPhaseGate();

        Assert.True(phases.TryMove(SawatariEventPhase.FirstCombat, SawatariEventPhase.Intermission));
        Assert.False(phases.TryMove(SawatariEventPhase.FirstCombat, SawatariEventPhase.Intermission));
        Assert.True(phases.TryMove(SawatariEventPhase.Intermission, SawatariEventPhase.DuelTransition));
        phases.FinalizeEvent();
        Assert.Equal(SawatariEventPhase.Finalizing, phases.Current);
    }
}
