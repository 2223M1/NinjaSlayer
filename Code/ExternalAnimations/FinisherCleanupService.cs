using System.Runtime.ExceptionServices;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class FinisherCleanupService
{
    internal static async Task CompleteAfterCardPlayed(Task original, CardPlay cardPlay)
    {
        Exception? originalFailure = null;
        try
        {
            await original;
        }
        catch (Exception ex)
        {
            originalFailure = ex;
        }

        Exception? completionFailure = null;
        try
        {
            if (FinisherSessionRegistry.GetPendingSession(cardPlay) is { } session)
            {
                await session.CompleteAsync(playPose: originalFailure == null);
            }
        }
        catch (Exception ex)
        {
            completionFailure = ex;
        }

        ThrowFailures(originalFailure, completionFailure);
    }

    internal static async Task CleanupAfterCardPlay(Task original, CardModel card)
    {
        Exception? originalFailure = null;
        try
        {
            await original;
        }
        catch (Exception ex)
        {
            originalFailure = ex;
        }

        Exception? completionFailure = null;
        try
        {
            if (FinisherSessionRegistry.GetPendingSession(card) is { } session)
            {
                await session.CompleteAsync(playPose: false);
            }
        }
        catch (Exception ex)
        {
            completionFailure = ex;
        }

        ThrowFailures(originalFailure, completionFailure);
    }

    private static void ThrowFailures(Exception? originalFailure, Exception? completionFailure)
    {
        if (originalFailure != null && completionFailure != null)
        {
            throw new AggregateException(
                "Card resolution and finisher completion both failed.",
                originalFailure,
                completionFailure);
        }

        if (originalFailure != null)
        {
            ExceptionDispatchInfo.Capture(originalFailure).Throw();
        }

        if (completionFailure != null)
        {
            ExceptionDispatchInfo.Capture(completionFailure).Throw();
        }
    }
}
