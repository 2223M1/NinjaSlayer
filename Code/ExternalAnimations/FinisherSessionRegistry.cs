using System.Diagnostics.CodeAnalysis;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class FinisherSessionRegistry
{
    private static FinisherSession? _active;
    private static FinisherSession? _pendingAfterCardPlayed;
    private static long _nextSessionId;

    internal static void TransferToAfterCardPlayed(FinisherSession session)
    {
        if (!ReferenceEquals(_active, session)
            || _pendingAfterCardPlayed != null
            || !session.CanTransferToAfterCardPlayed)
        {
            throw new InvalidOperationException("A NinjaSlayer finisher is already awaiting AfterCardPlayed.");
        }

        _pendingAfterCardPlayed = session;
    }

    internal static FinisherSession? GetActiveSession() => _active;

    internal static bool HasRegisteredSessionForCombat(
        ICombatState combatState,
        NCombatRoom room)
    {
        FinisherSession? session = _active ?? _pendingAfterCardPlayed;
        if (session == null)
        {
            return false;
        }

        if (ReferenceEquals(session.CombatState, combatState)
            && ReferenceEquals(session.Room, room))
        {
            return true;
        }

        DetachRegisteredSession();
        ReleaseStaleSession(session);
        return false;
    }

    internal static FinisherSession? GetPendingSession(CardPlay cardPlay) =>
        _pendingAfterCardPlayed?.CardPlay == cardPlay ? _pendingAfterCardPlayed : null;

    internal static FinisherSession? GetPendingSession(CardModel card) =>
        _pendingAfterCardPlayed?.CardPlay?.Card == card ? _pendingAfterCardPlayed : null;

    internal static bool TryRegisterSession(
        FinisherSessionRequest request,
        ICombatState combatState,
        NCombatRoom room,
        [NotNullWhen(true)] out FinisherSession? session)
    {
        if (_active != null || _pendingAfterCardPlayed != null)
        {
            session = null;
            return false;
        }

        long sessionId = _nextSessionId + 1;
        try
        {
            session = new FinisherSession(
                sessionId,
                combatState,
                room,
                request);
        }
        catch
        {
            request.Camera.Dispose();
            throw;
        }

        _nextSessionId = sessionId;
        _active = session;
        return true;
    }

    internal static bool IsSessionCurrent(FinisherSession session) =>
        ReferenceEquals(_active, session)
        || ReferenceEquals(_pendingAfterCardPlayed, session);

    internal static void UnregisterSession(FinisherSession session)
    {
        if (ReferenceEquals(_active, session))
        {
            _active = null;
        }

        if (ReferenceEquals(_pendingAfterCardPlayed, session))
        {
            _pendingAfterCardPlayed = null;
        }
    }

    private static void DetachRegisteredSession()
    {
        _active = null;
        _pendingAfterCardPlayed = null;
    }

    private static void ReleaseStaleSession(FinisherSession session)
    {
        Entry.Logger.Warn(
            $"Released stale finisher session {session.SessionId} before entering a new combat.");
        _ = CancelStaleSession(session);
    }

    private static async Task CancelStaleSession(FinisherSession session)
    {
        try
        {
            await session.CancelAsync();
        }
        catch (Exception ex)
        {
            Entry.Logger.Error(
                $"Stale finisher session {session.SessionId} cleanup failed: {ex}");
        }
    }
}
