using System.Diagnostics.CodeAnalysis;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class FinisherSessionRegistry
{
    private static readonly object SessionRegistrySync = new();
    private static FinisherSession? _active;
    private static FinisherSession? _pendingAfterCardPlayed;
    private static ICombatState? _epochCombatState;
    private static NCombatRoom? _epochRoom;
    private static long _nextSessionId;
    private static long _combatEpoch;
    private static long _registryGeneration;

    internal static void TransferToAfterCardPlayed(FinisherSession session)
    {
        lock (SessionRegistrySync)
        {
            if (!ReferenceEquals(_active, session)
                || _pendingAfterCardPlayed != null
                || !session.TryAwaitPostCard())
            {
                throw new InvalidOperationException("A NinjaSlayer finisher is already awaiting AfterCardPlayed.");
            }

            _pendingAfterCardPlayed = session;
        }
    }

    internal static FinisherSession? GetActiveSession()
    {
        lock (SessionRegistrySync)
        {
            return _active;
        }
    }

    internal static bool HasRegisteredSessionForCombat(
        ICombatState combatState,
        NCombatRoom room)
    {
        FinisherSession? staleSession = null;
        lock (SessionRegistrySync)
        {
            if (_active == null && _pendingAfterCardPlayed == null)
            {
                return false;
            }

            if (ReferenceEquals(_epochCombatState, combatState)
                && ReferenceEquals(_epochRoom, room))
            {
                return true;
            }

            staleSession = DetachRegisteredSession(combatState, room);
        }

        ReleaseStaleSession(
            staleSession,
            "A new combat replaced this finisher session before its normal cleanup completed.");

        return false;
    }

    internal static FinisherSession? GetPendingSession(CardPlay cardPlay)
    {
        lock (SessionRegistrySync)
        {
            return _pendingAfterCardPlayed?.CardPlay == cardPlay ? _pendingAfterCardPlayed : null;
        }
    }

    internal static FinisherSession? GetPendingSession(CardModel card)
    {
        lock (SessionRegistrySync)
        {
            return _pendingAfterCardPlayed?.CardPlay?.Card == card ? _pendingAfterCardPlayed : null;
        }
    }

    internal static bool TryRegisterSession(
        FinisherSessionRequest request,
        ICombatState combatState,
        NCombatRoom room,
        [NotNullWhen(true)] out FinisherSession? session)
    {
        lock (SessionRegistrySync)
        {
            if (_active != null || _pendingAfterCardPlayed != null)
            {
                session = null;
                return false;
            }

            bool startsNewEpoch = !ReferenceEquals(_epochCombatState, combatState)
                || !ReferenceEquals(_epochRoom, room);
            long combatEpoch = startsNewEpoch ? _combatEpoch + 1 : _combatEpoch;
            long sessionId = _nextSessionId + 1;
            long registryGeneration = _registryGeneration + 1;
            try
            {
                session = new FinisherSession(
                    sessionId,
                    combatEpoch,
                    registryGeneration,
                    combatState,
                    room,
                    request);
            }
            catch
            {
                request.Camera.Dispose();
                throw;
            }

            if (startsNewEpoch)
            {
                _epochCombatState = combatState;
                _epochRoom = room;
                _combatEpoch = combatEpoch;
            }

            _nextSessionId = sessionId;
            _registryGeneration = registryGeneration;
            _active = session;
            return true;
        }
    }

    internal static bool IsSessionCurrent(FinisherSession session)
    {
        lock (SessionRegistrySync)
        {
            return session.RegistryGeneration == _registryGeneration
                && (ReferenceEquals(_active, session) || ReferenceEquals(_pendingAfterCardPlayed, session));
        }
    }

    internal static void MarkSessionCompleting(FinisherSession session)
    {
        lock (SessionRegistrySync)
        {
            if (ReferenceEquals(_pendingAfterCardPlayed, session))
            {
                _pendingAfterCardPlayed = null;
            }
        }
    }

    internal static void UnregisterSession(FinisherSession session)
    {
        lock (SessionRegistrySync)
        {
            bool changed = false;
            if (ReferenceEquals(_active, session))
            {
                _active = null;
                changed = true;
            }

            if (ReferenceEquals(_pendingAfterCardPlayed, session))
            {
                _pendingAfterCardPlayed = null;
                changed = true;
            }

            if (changed)
            {
                _registryGeneration++;
            }
        }
    }

    private static FinisherSession? DetachRegisteredSession(
        ICombatState? combatState,
        NCombatRoom? room)
    {
        FinisherSession? staleSession = _active ?? _pendingAfterCardPlayed;
        _active = null;
        _pendingAfterCardPlayed = null;
        _epochCombatState = combatState;
        _epochRoom = room;
        _combatEpoch++;
        _registryGeneration++;
        return staleSession;
    }

    private static void ReleaseStaleSession(FinisherSession? session, string reason)
    {
        if (session == null)
        {
            return;
        }

        Entry.Logger.Warn(
            $"Released stale finisher session {session.SessionId} before entering a new combat epoch.");
        TaskHelper.RunSafely(session.CompleteAsync(
            FinisherCompletionStatus.Cancelled,
            FinisherCompletionMode.ReleaseOnly,
            reason));
    }
}
