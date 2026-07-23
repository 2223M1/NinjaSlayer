using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class FinisherCleanupService
{
    internal static async Task CompleteAfterCardPlayed(Task original, CardPlay cardPlay)
    {
        try
        {
            await original;
        }
        catch
        {
            await CleanupPending(cardPlay.Card, playPose: false);
            throw;
        }

        if (FinisherSessionRegistry.GetPendingSession(cardPlay) != null)
        {
            await CleanupPending(cardPlay.Card, playPose: true);
        }
    }

    internal static async Task CleanupAfterCardPlay(Task original, CardModel card)
    {
        try
        {
            await original;
        }
        finally
        {
            if (FinisherSessionRegistry.GetPendingSession(card) != null)
            {
                await CleanupPending(card, playPose: false);
            }
        }
    }

    private static async Task CleanupPending(CardModel card, bool playPose)
    {
        FinisherSession? session = FinisherSessionRegistry.GetPendingSession(card);
        if (session == null)
        {
            return;
        }

        await session.CompleteAsync(
            playPose ? FinisherCompletionStatus.Succeeded : FinisherCompletionStatus.Degraded,
            playPose ? FinisherCompletionMode.PlayPose : FinisherCompletionMode.CommitWithoutPose,
            playPose ? null : "Card resolution ended before AfterCardPlayed completed.");
    }
}
