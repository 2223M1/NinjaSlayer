using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.Transition;

public static class NinjaSlayerTransitionLoadSmoothing
{
    internal const int FinalizeBatchSize = 1;
    internal const long NoGcRegionBudgetBytes = 256L * 1024 * 1024;

    private static readonly object SyncRoot = new();
    private static readonly TransitionGcDeferralState GcDeferral = new();
    private static readonly TransitionNoGcRegionState NoGcRegion = new();
    private static long animationSessionId;
    private static TransitionPerformanceTrace? activeTrace;

    internal static bool IsAnimationPlaying => Volatile.Read(ref animationSessionId) != 0;

    public static int GetConcurrentAssetLoadLimit() =>
        TransitionLoadConcurrencyPolicy.Resolve(IsAnimationPlaying);

    internal static TransitionPerformanceTrace BeginSession(long sessionId, TransitionInvocationKind kind)
    {
        TransitionGcCounts beforeNoGcRequest = TransitionGcCounts.Capture();
        int carriedRequestCount = GcDeferral.Begin(sessionId);
        NoGcRegion.Begin(
            sessionId,
            NoGcRegionBudgetBytes,
            beforeNoGcRequest,
            TryStartRuntimeNoGcRegion,
            TransitionGcCounts.Capture);
        var trace = new TransitionPerformanceTrace(
            sessionId,
            kind,
            TransitionGcCounts.Capture());
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

    internal static void RecordPhase(string name, TimeSpan elapsed)
    {
        long sessionId = Volatile.Read(ref animationSessionId);
        if (sessionId != 0)
        {
            GetActiveTrace(sessionId)?.RecordPhase(name, elapsed);
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

    internal static TransitionLoadSmoothingCompletion CompleteSession(
        long sessionId,
        TransitionGcCounts endingGcCounts)
    {
        EndAnimation(sessionId);
        TransitionGcDeferralCompletion completion = GcDeferral.Complete(sessionId);
        if (!completion.IsCurrentSession)
        {
            return TransitionLoadSmoothingCompletion.None;
        }

        TransitionNoGcRegionCompletion noGcRegion = NoGcRegion.Complete(
            sessionId,
            endingGcCounts,
            TransitionNoGcRegionExecutor.IsRuntimeRegionActive,
            GC.EndNoGCRegion);

        lock (SyncRoot)
        {
            if (activeTrace?.SessionId == sessionId)
            {
                activeTrace = null;
            }
        }

        TransitionGcFlushResult gcFlush = TransitionGcRequestExecutor.Execute(
            completion.DeferredRequestCount,
            RequestOptimizedNonBlockingCollection);
        return new TransitionLoadSmoothingCompletion(gcFlush, noGcRegion);
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

    private static bool TryStartRuntimeNoGcRegion() =>
        GC.TryStartNoGCRegion(
            NoGcRegionBudgetBytes,
            disallowFullBlockingGC: true);
}

internal readonly record struct TransitionLoadSmoothingCompletion(
    TransitionGcFlushResult GcFlush,
    TransitionNoGcRegionCompletion NoGcRegion)
{
    public static TransitionLoadSmoothingCompletion None { get; } = new(
        TransitionGcFlushResult.None,
        TransitionNoGcRegionCompletion.None);
}
