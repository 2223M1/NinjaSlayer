namespace NinjaSlayer.Code.Combat;

public enum BossBurstCombatEndMusicDecision
{
    PassThrough,
    SuppressAndRestoreActMusic,
    Suppress
}

public enum BossBurstGroupedDeathFadeDecision
{
    PassThrough,
    FilterParticipants,
    SuppressPlaybackForCleanup
}

internal static class BossBurstPresentationPolicy
{
    public static BossBurstCombatEndMusicDecision ResolveCombatEndMusic(
        bool hasParticipant,
        bool isCurrentRoom,
        bool isBossRoom,
        bool combatInProgress,
        bool actMusicRestoreAttempted)
    {
        if (!hasParticipant || !isCurrentRoom || !isBossRoom || combatInProgress)
        {
            return BossBurstCombatEndMusicDecision.PassThrough;
        }

        return actMusicRestoreAttempted
            ? BossBurstCombatEndMusicDecision.Suppress
            : BossBurstCombatEndMusicDecision.SuppressAndRestoreActMusic;
    }

    public static bool ShouldSuppressDeathFade(
        bool hasParticipant,
        bool isCurrentRoom) =>
        hasParticipant && isCurrentRoom;

    public static BossBurstGroupedDeathFadeDecision ResolveGroupedDeathFade(
        int creatureCount,
        int participantCount)
    {
        if (creatureCount <= 0 || participantCount <= 0 || participantCount > creatureCount)
        {
            return BossBurstGroupedDeathFadeDecision.PassThrough;
        }

        return participantCount < creatureCount
            ? BossBurstGroupedDeathFadeDecision.FilterParticipants
            : BossBurstGroupedDeathFadeDecision.SuppressPlaybackForCleanup;
    }

    public static bool ShouldRollbackMusic(
        bool ownsMusicTransition,
        bool hasRemainingParticipants) =>
        ownsMusicTransition && !hasRemainingParticipants;
}
