using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.LogicTests;

public sealed class BossBurstPresentationPolicyTests
{
    [Theory]
    [InlineData(true, true, true, false, false, BossBurstCombatEndMusicDecision.SuppressAndRestoreActMusic)]
    [InlineData(true, true, true, false, true, BossBurstCombatEndMusicDecision.Suppress)]
    [InlineData(false, true, true, false, false, BossBurstCombatEndMusicDecision.PassThrough)]
    [InlineData(true, false, true, false, false, BossBurstCombatEndMusicDecision.PassThrough)]
    [InlineData(true, true, false, false, false, BossBurstCombatEndMusicDecision.PassThrough)]
    [InlineData(true, true, true, true, false, BossBurstCombatEndMusicDecision.PassThrough)]
    public void CombatEndMusicIsScopedToAnExplodingBossRewardRoom(
        bool hasParticipant,
        bool isCurrentRoom,
        bool isBossRoom,
        bool combatInProgress,
        bool actMusicRestoreAttempted,
        BossBurstCombatEndMusicDecision expected)
    {
        Assert.Equal(
            expected,
            BossBurstPresentationPolicy.ResolveCombatEndMusic(
                hasParticipant,
                isCurrentRoom,
                isBossRoom,
                combatInProgress,
                actMusicRestoreAttempted));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    public void DeathFadeRequiresTheMarkedCreatureAndCurrentRoom(
        bool hasParticipant,
        bool isCurrentRoom,
        bool expected)
    {
        Assert.Equal(
            expected,
            BossBurstPresentationPolicy.ShouldSuppressDeathFade(
                hasParticipant,
                isCurrentRoom));
    }

    [Theory]
    [InlineData(0, 0, BossBurstGroupedDeathFadeDecision.PassThrough)]
    [InlineData(2, 0, BossBurstGroupedDeathFadeDecision.PassThrough)]
    [InlineData(2, 1, BossBurstGroupedDeathFadeDecision.FilterParticipants)]
    [InlineData(2, 2, BossBurstGroupedDeathFadeDecision.SuppressPlaybackForCleanup)]
    [InlineData(2, 3, BossBurstGroupedDeathFadeDecision.PassThrough)]
    public void GroupedDeathFadeFiltersParticipantsWithoutBreakingCallerCleanup(
        int creatureCount,
        int participantCount,
        BossBurstGroupedDeathFadeDecision expected)
    {
        Assert.Equal(
            expected,
            BossBurstPresentationPolicy.ResolveGroupedDeathFade(
                creatureCount,
                participantCount));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public void MusicRollbackRequiresOwnershipAndAnEmptyPresentation(
        bool ownsMusicTransition,
        bool hasRemainingParticipants,
        bool expected)
    {
        Assert.Equal(
            expected,
            BossBurstPresentationPolicy.ShouldRollbackMusic(
                ownsMusicTransition,
                hasRemainingParticipants));
    }
}
