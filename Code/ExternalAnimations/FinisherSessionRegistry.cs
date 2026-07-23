using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

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

    internal static bool HasRegisteredSession()
    {
        lock (SessionRegistrySync)
        {
            return _active != null || _pendingAfterCardPlayed != null;
        }
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
            return _pendingAfterCardPlayed?.CardPlay.Card == card ? _pendingAfterCardPlayed : null;
        }
    }

    internal static bool TryRegisterSession(
        Creature owner,
        NCreature ownerNode,
        NCreature focusNode,
        IEnumerable<Creature> victims,
        CombatCinematicCameraLease camera,
        CardPlay cardPlay,
        bool requiresAfterCardPlayed,
        int resolvedHits,
        ICombatState combatState,
        NCombatRoom room,
        out FinisherSession? session)
    {
        lock (SessionRegistrySync)
        {
            if (_active != null || _pendingAfterCardPlayed != null)
            {
                session = null;
                return false;
            }

            if (!ReferenceEquals(_epochCombatState, combatState) || !ReferenceEquals(_epochRoom, room))
            {
                _epochCombatState = combatState;
                _epochRoom = room;
                _combatEpoch++;
            }

            long sessionId = ++_nextSessionId;
            long registryGeneration = ++_registryGeneration;
            try
            {
                session = new FinisherSession(
                    sessionId,
                    _combatEpoch,
                    registryGeneration,
                    combatState,
                    room,
                    owner,
                    ownerNode,
                    focusNode,
                    victims,
                    camera,
                    cardPlay,
                    requiresAfterCardPlayed,
                    resolvedHits);
            }
            catch (Exception ex)
            {
                session = null;
                FinisherLog.Warn($"Could not create NinjaSlayer finisher session {sessionId}: {ex}");
                return false;
            }

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
}
