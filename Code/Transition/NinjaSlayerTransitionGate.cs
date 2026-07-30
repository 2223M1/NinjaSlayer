using MegaCrit.Sts2.Core.Nodes;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.Transition;

internal static class NinjaSlayerTransitionGate
{
    private static readonly object SyncRoot = new();
    private static NinjaSlayerTransitionSession? _activeSession;
    private static bool _pending;
    private static int _activeSessionPresent;

    /// <summary>
    /// Lock-free mirror of <c>_activeSession != null</c>. The presentation-barrier patches sit on
    /// audio entry points that fire many times per second during ordinary combat; without this a
    /// closure allocation and a global lock were paid on every one of them, even though no
    /// transition was running and the deferral could never succeed.
    /// </summary>
    internal static bool HasActiveSession => Volatile.Read(ref _activeSessionPresent) != 0;

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
        TransitionInvocationKind invocationKind,
        Func<NinjaSlayerTransitionSession, CancellationToken, Task> startAnimation,
        CancellationToken cancellationToken,
        out NinjaSlayerTransitionSession? session)
    {
        var next = new NinjaSlayerTransitionSession(
            new TransitionViewAdapter(transition),
            invocationKind,
            cancellationToken);
        NinjaSlayerTransitionSession? previous;
        lock (SyncRoot)
        {
            previous = _activeSession;
            _activeSession = next;
            Volatile.Write(ref _activeSessionPresent, 1);
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

    internal static bool TryAttachPresentationRoot(NRun root)
    {
        NinjaSlayerTransitionSession? session;
        lock (SyncRoot)
        {
            session = _activeSession;
        }

        if (session is null)
        {
            return false;
        }

        if (session.TryAttachPresentationRoot(root))
        {
            return true;
        }

        _ = session.CompleteAsync(
            TransitionCompletionStatus.Cancelled,
            forceRelease: true,
            "A replacement NRun appeared before the staged Transition presentation completed.");
        return false;
    }

    internal static bool TryDeferPresentation(Action operation)
    {
        if (!HasActiveSession)
        {
            return false;
        }

        NinjaSlayerTransitionSession? session;
        lock (SyncRoot)
        {
            session = _activeSession;
        }

        return session?.TryDeferPresentation(operation) == true;
    }

    internal static bool TryDeferPresentation(Func<Task> operation, out Task completion)
    {
        if (!HasActiveSession)
        {
            completion = Task.CompletedTask;
            return false;
        }

        NinjaSlayerTransitionSession? session;
        lock (SyncRoot)
        {
            session = _activeSession;
        }

        if (session is not null && session.TryDeferPresentation(operation, out completion))
        {
            return true;
        }

        completion = Task.CompletedTask;
        return false;
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

    internal static void CancelActiveSession(string diagnostic)
    {
        NinjaSlayerTransitionSession? session;
        lock (SyncRoot)
        {
            _pending = false;
            session = _activeSession;
        }

        if (session is not null)
        {
            _ = session.CompleteAsync(
                TransitionCompletionStatus.Cancelled,
                forceRelease: true,
                diagnostic);
        }
    }

    internal static (bool Active, bool Pending) GetHealthState()
    {
        lock (SyncRoot)
        {
            return (_activeSession is not null, _pending);
        }
    }

    internal static void OnSessionCompleted(NinjaSlayerTransitionSession session)
    {
        lock (SyncRoot)
        {
            if (ReferenceEquals(_activeSession, session))
            {
                _activeSession = null;
                Volatile.Write(ref _activeSessionPresent, 0);
            }
        }
    }
}
