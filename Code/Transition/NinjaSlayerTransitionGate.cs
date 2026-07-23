using MegaCrit.Sts2.Core.Nodes;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.Transition;

internal static class NinjaSlayerTransitionGate
{
    private static readonly object SyncRoot = new();
    private static NinjaSlayerTransitionSession? _activeSession;
    private static bool _pending;

    internal static bool Pending
    {
        get
        {
            lock (SyncRoot)
            {
                return _pending;
            }
        }
        set
        {
            lock (SyncRoot)
            {
                _pending = value;
            }
        }
    }

    /// <summary>
    /// Registers the session before its animation factory can mutate transition UI. A synchronous
    /// start failure therefore still owns enough state to restore input and fall back to FadeOut.
    /// </summary>
    internal static bool TryStartSession(
        NTransition transition,
        CancellationToken cancellationToken,
        Func<NinjaSlayerTransitionSession, CancellationToken, Task> startAnimation,
        out NinjaSlayerTransitionSession? session)
    {
        var next = new NinjaSlayerTransitionSession(transition, cancellationToken);
        NinjaSlayerTransitionSession? previous;
        lock (SyncRoot)
        {
            previous = _activeSession;
            _activeSession = next;
        }

        if (previous != null)
        {
            _ = previous.CompleteAsync(
                TransitionCompletionStatus.Superseded,
                forceRelease: true,
                "A newer transition session superseded this session.");
        }

        try
        {
            next.Start(startAnimation);
            session = next;
            return true;
        }
        catch (Exception ex)
        {
            Entry.Logger.Error($"NinjaSlayer transition failed during synchronous startup: {ex}");
            _ = next.CompleteAsync(TransitionCompletionStatus.Faulted, forceRelease: true, ex.ToString());
            session = null;
            return false;
        }
    }

    internal static bool TryClaimReveal(NTransition transition, out NinjaSlayerTransitionSession? session)
    {
        lock (SyncRoot)
        {
            NinjaSlayerTransitionSession? active = _activeSession;
            if (active != null
                && ReferenceEquals(active.Transition, transition)
                && active.TryClaimReveal())
            {
                session = active;
                return true;
            }

            session = null;
            return false;
        }
    }

    internal static bool ConsumePendingRequest()
    {
        lock (SyncRoot)
        {
            bool pending = _pending;
            _pending = false;
            return pending;
        }
    }

    internal static void CancelPendingRequest() => Pending = false;

    internal static void OnSessionCompleted(NinjaSlayerTransitionSession session)
    {
        lock (SyncRoot)
        {
            if (ReferenceEquals(_activeSession, session))
            {
                _activeSession = null;
            }
        }
    }
}
