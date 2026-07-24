using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.Transition;

public static class NinjaSlayerTransitionLoadSmoothing
{
    internal const int FinalizeBatchSize = 1;

    private static readonly object SyncRoot = new();
    private static readonly TransitionGcDeferralState GcDeferral = new();
    private static long animationSessionId;
    private static TransitionPerformanceTrace? activeTrace;

    internal static bool IsAnimationPlaying => Volatile.Read(ref animationSessionId) != 0;

    internal static TransitionPerformanceTrace BeginSession(long sessionId, TransitionInvocationKind kind)
    {
        var trace = new TransitionPerformanceTrace(sessionId, kind);
        int carriedRequestCount = GcDeferral.Begin(sessionId);
        lock (SyncRoot)
        {
            activeTrace = trace;
            Volatile.Write(ref animationSessionId, sessionId);
        }

        if (carriedRequestCount > 0)
        {
            Entry.Logger.Warn(
                $"NinjaSlayer transition session {sessionId} inherited {carriedRequestCount} deferred GC request(s) " +
                "from an incompletely released session.");
        }

        return trace;
    }

    internal static void RecordFinalizeBatch(int count, TimeSpan elapsed)
    {
        long sessionId = Volatile.Read(ref animationSessionId);
        if (sessionId != 0)
        {
            GetActiveTrace(sessionId)?.RecordFinalizeBatch(count, elapsed);
        }
    }

    public static void CollectWhenSafe()
    {
        if (GcDeferral.TryDefer(out long sessionId, out _))
        {
            GetActiveTrace(sessionId)?.RecordDeferredGc();
            return;
        }

        GC.Collect();
    }

    internal static void EndAnimation(long sessionId)
    {
        Interlocked.CompareExchange(ref animationSessionId, 0, sessionId);
    }

    internal static TransitionGcFlushResult CompleteSession(long sessionId)
    {
        EndAnimation(sessionId);
        TransitionGcDeferralCompletion completion = GcDeferral.Complete(sessionId);
        if (!completion.IsCurrentSession)
        {
            return TransitionGcFlushResult.None;
        }

        lock (SyncRoot)
        {
            if (activeTrace?.SessionId == sessionId)
            {
                activeTrace = null;
            }
        }

        return TransitionGcRequestExecutor.Execute(
            completion.DeferredRequestCount,
            RequestOptimizedNonBlockingCollection);
    }

    private static TransitionPerformanceTrace? GetActiveTrace(long sessionId)
    {
        lock (SyncRoot)
        {
            return activeTrace?.SessionId == sessionId ? activeTrace : null;
        }
    }

    private static void RequestOptimizedNonBlockingCollection()
    {
        GC.Collect(
            GC.MaxGeneration,
            GCCollectionMode.Optimized,
            blocking: false,
            compacting: false);
    }
}
