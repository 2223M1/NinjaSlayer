using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.Transition;

public static class NinjaSlayerTransitionLoadSmoothing
{
    /// <summary>
    /// Always finalize at least one queued resource so the drain cannot stall, then keep going
    /// while the batch stays inside <see cref="FinalizeBatchBudget"/>. A fixed count could not do
    /// both jobs: one cheap resource per call left most of the queue to drain in a burst at the
    /// reveal, while a larger count would let a single expensive resource overrun the frame.
    /// </summary>
    internal const int FinalizeBatchMinimum = 1;

    internal static readonly TimeSpan FinalizeBatchBudget = TimeSpan.FromMilliseconds(2);

    internal const long NoGcRegionBudgetBytes = 256L * 1024 * 1024;

    private static readonly TransitionGcDeferralState GcDeferral = new();
    private static readonly TransitionNoGcRegionState NoGcRegion = new();
    private static long animationSessionId;

    internal static bool IsAnimationPlaying => Volatile.Read(ref animationSessionId) != 0;

    public static int GetConcurrentAssetLoadLimit() => IsAnimationPlaying ? 8 : 128;

    internal static void BeginSession(long sessionId)
    {
        bool inheritedRequest = GcDeferral.Begin(sessionId);
        NoGcRegion.Begin(
            sessionId,
            TryStartRuntimeNoGcRegion,
            TransitionGcCounts.Capture);
        Volatile.Write(ref animationSessionId, sessionId);

        if (inheritedRequest)
        {
            Entry.Logger.Warn(
                $"NinjaSlayer transition session {sessionId} inherited a deferred GC request " +
                "from an incompletely released session.");
        }

    }

    public static void CollectWhenSafe()
    {
        if (GcDeferral.TryDefer())
        {
            return;
        }

        GC.Collect();
    }

    internal static void EndAnimation(long sessionId)
    {
        Interlocked.CompareExchange(ref animationSessionId, 0, sessionId);
    }

    internal static Exception? CompleteSession(
        long sessionId,
        TransitionGcCounts endingGcCounts)
    {
        EndAnimation(sessionId);
        NoGcRegion.Complete(
            sessionId,
            endingGcCounts,
            TransitionNoGcRegionState.IsRuntimeRegionActive,
            GC.EndNoGCRegion);
        return GcDeferral.Complete(sessionId, RequestOptimizedNonBlockingCollection);
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
