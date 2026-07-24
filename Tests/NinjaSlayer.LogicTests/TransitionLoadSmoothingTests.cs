using NinjaSlayer.Code.Transition;

namespace NinjaSlayer.LogicTests;

public sealed class TransitionLoadSmoothingTests
{
    [Fact]
    public void VisibleTransitionUsesBoundedAssetLoadConcurrency()
    {
        Assert.Equal(
            TransitionLoadConcurrencyPolicy.VisibleTransitionConcurrentLoadLimit,
            TransitionLoadConcurrencyPolicy.Resolve(transitionVisible: true));
        Assert.Equal(
            TransitionLoadConcurrencyPolicy.VanillaConcurrentLoadLimit,
            TransitionLoadConcurrencyPolicy.Resolve(transitionVisible: false));
        Assert.True(
            TransitionLoadConcurrencyPolicy.VisibleTransitionConcurrentLoadLimit
            < TransitionLoadConcurrencyPolicy.VanillaConcurrentLoadLimit);
    }

    [Fact]
    public void DeferredRequestsAreOwnedAndCoalescedBySession()
    {
        var state = new TransitionGcDeferralState();
        Assert.Equal(0, state.Begin(17));

        for (var ordinal = 1; ordinal <= 3; ordinal++)
        {
            Assert.True(state.TryDefer(out long sessionId, out int requestOrdinal));
            Assert.Equal(17, sessionId);
            Assert.Equal(ordinal, requestOrdinal);
        }

        TransitionGcDeferralCompletion completion = state.Complete(17);
        Assert.True(completion.IsCurrentSession);
        Assert.Equal(3, completion.DeferredRequestCount);
        Assert.False(state.IsActive);
        Assert.False(state.Complete(17).IsCurrentSession);
    }

    [Fact]
    public void StaleSessionCannotCompleteOrDisableCurrentSession()
    {
        var state = new TransitionGcDeferralState();
        state.Begin(20);
        Assert.True(state.TryDefer(out _, out _));

        Assert.Equal(1, state.Begin(21));
        Assert.False(state.Complete(20).IsCurrentSession);
        Assert.True(state.IsActive);
        Assert.True(state.TryDefer(out long sessionId, out int requestOrdinal));
        Assert.Equal(21, sessionId);
        Assert.Equal(2, requestOrdinal);

        TransitionGcDeferralCompletion current = state.Complete(21);
        Assert.True(current.IsCurrentSession);
        Assert.Equal(2, current.DeferredRequestCount);
    }

    [Fact]
    public void ZeroDeferredRequestsDoNotInvokeCollector()
    {
        var calls = 0;
        TransitionGcFlushResult result = TransitionGcRequestExecutor.Execute(0, () => calls++);

        Assert.Equal(0, calls);
        Assert.False(result.Attempted);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void CollectorFailureIsCapturedWithoutFallbackInvocation()
    {
        var calls = 0;
        TransitionGcFlushResult result = TransitionGcRequestExecutor.Execute(3, () =>
        {
            calls++;
            throw new InvalidOperationException("background collection unavailable");
        });

        Assert.Equal(1, calls);
        Assert.Equal(3, result.DeferredRequestCount);
        Assert.True(result.Attempted);
        Assert.False(result.Succeeded);
        Assert.Equal(nameof(InvalidOperationException), result.ErrorType);
    }

    [Fact]
    public void NoGcRegionIsOwnedAndEndedExactlyOnceByItsSession()
    {
        var state = new TransitionNoGcRegionState();
        var starts = 0;
        var ends = 0;
        var counts = new TransitionGcCounts(3, 2, 1);

        TransitionNoGcRegionStartResult start = state.Begin(
            31,
            256L * 1024 * 1024,
            counts,
            () =>
            {
                starts++;
                return true;
            },
            () => counts);
        TransitionNoGcRegionCompletion completion = state.Complete(
            31,
            counts,
            () => true,
            () => ends++);

        Assert.True(start.Started);
        Assert.Equal(1, starts);
        Assert.True(completion.IsCurrentSession);
        Assert.True(completion.EndSucceeded);
        Assert.Equal(1, ends);
        Assert.False(state.Complete(31, counts, () => true, () => ends++).IsCurrentSession);
        Assert.Equal(1, ends);
    }

    [Fact]
    public void SupersedingSessionInheritsNoGcRegionAndStaleOwnerCannotEndIt()
    {
        var state = new TransitionNoGcRegionState();
        var starts = 0;
        var ends = 0;
        var counts = new TransitionGcCounts(0, 0, 0);

        state.Begin(40, 1024, counts, () =>
        {
            starts++;
            return true;
        }, () => counts);
        TransitionNoGcRegionStartResult inherited = state.Begin(41, 1024, counts, () =>
        {
            starts++;
            return true;
        }, () => counts);

        Assert.True(inherited.Started);
        Assert.True(inherited.Inherited);
        Assert.Equal(1, starts);
        Assert.False(state.Complete(40, counts, () => true, () => ends++).IsCurrentSession);
        Assert.Equal(0, ends);
        Assert.True(state.Complete(41, counts, () => true, () => ends++).EndSucceeded);
        Assert.Equal(1, ends);
    }

    [Fact]
    public void CollectionDuringNoGcRegionMarksBudgetExhaustedWithoutEndingAnotherRegion()
    {
        var state = new TransitionNoGcRegionState();
        var activeChecks = 0;
        var ends = 0;
        state.Begin(
            50,
            1024,
            new TransitionGcCounts(1, 1, 0),
            () => true,
            () => new TransitionGcCounts(1, 1, 0));

        TransitionNoGcRegionCompletion completion = state.Complete(
            50,
            new TransitionGcCounts(2, 1, 0),
            () =>
            {
                activeChecks++;
                return true;
            },
            () => ends++);

        Assert.True(completion.CollectionObserved);
        Assert.False(completion.EndAttempted);
        Assert.Equal(0, activeChecks);
        Assert.Equal(0, ends);
    }

    [Fact]
    public void NoGcStartupCollectionBecomesTheProtectedIntervalBaseline()
    {
        var state = new TransitionNoGcRegionState();
        var counts = new TransitionGcCounts(7, 4, 2);

        TransitionNoGcRegionStartResult start = state.Begin(
            51,
            1024,
            new TransitionGcCounts(6, 3, 1),
            () => true,
            () => counts);
        TransitionNoGcRegionCompletion completion = state.Complete(
            51,
            counts,
            () => true,
            () => { });

        Assert.Equal(counts, start.StartingGcCounts);
        Assert.False(completion.CollectionObserved);
        Assert.True(completion.EndSucceeded);
    }

    [Fact]
    public void FrameMetricsUseCumulativeSlowFrameThresholds()
    {
        var metrics = new TransitionFrameMetrics();
        foreach (double delta in new[] { -1, 0, double.NaN, 0.016, 0.025, 0.026, 0.041, 0.061 })
        {
            metrics.Record(delta);
        }

        TransitionFrameMetricsSnapshot snapshot = metrics.Snapshot();
        Assert.Equal(5, snapshot.FrameCount);
        Assert.Equal(61, snapshot.LongestFrameMilliseconds, precision: 6);
        Assert.Equal(3, snapshot.Over25Milliseconds);
        Assert.Equal(2, snapshot.Over40Milliseconds);
        Assert.Equal(1, snapshot.Over60Milliseconds);
    }

    [Fact]
    public void PerformanceTraceRecordsNamedPhaseTiming()
    {
        var trace = new TransitionPerformanceTrace(
            sessionId: 12,
            TransitionInvocationKind.Embark,
            new TransitionGcCounts(0, 0, 0));

        trace.RecordPhase("nrun_enter_tree", TimeSpan.FromMilliseconds(42));
        TransitionPerformanceSnapshot snapshot = trace.Complete(
            TransitionCompletionStatus.Succeeded,
            new TransitionGcCounts(0, 0, 0),
            TransitionGcFlushResult.None);

        TransitionPhaseSample phase = Assert.Single(snapshot.PhaseSamples);
        Assert.Equal("nrun_enter_tree", phase.Name);
        Assert.Equal(42, phase.DurationMilliseconds, precision: 3);
        Assert.Contains("phases=nrun_enter_tree@", snapshot.ToLogMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void PerformanceSnapshotIncludesVideoLoadingGcAndTerminalStatus()
    {
        var trace = new TransitionPerformanceTrace(
            29,
            TransitionInvocationKind.SaveLoad,
            new TransitionGcCounts(5, 3, 1));
        trace.RecordStreamAcquire(TimeSpan.FromMilliseconds(4));
        trace.RecordPlayCall(TimeSpan.FromMilliseconds(2));
        trace.MarkVideoStarted();
        trace.RecordFrame(0.016);
        trace.RecordFrame(0.061, videoPositionSeconds: 0.75);
        trace.RecordFirstPostPlayFrame();
        trace.RecordFinalizeBatch(1, TimeSpan.FromMilliseconds(0.2));
        trace.RecordDeferredGc();
        trace.RecordDeferredGc();
        trace.MarkVideoStopped();

        var flush = new TransitionGcFlushResult(2, true, true, 0.3, null);
        TransitionPerformanceSnapshot snapshot = trace.Complete(
            TransitionCompletionStatus.Cancelled,
            new TransitionGcCounts(8, 4, 2),
            flush);

        Assert.Equal(29, snapshot.SessionId);
        Assert.Equal(TransitionInvocationKind.SaveLoad, snapshot.Kind);
        Assert.Equal(TransitionCompletionStatus.Cancelled, snapshot.Status);
        Assert.Equal(2, snapshot.SessionFrames.FrameCount);
        Assert.Equal(2, snapshot.VideoFrames.FrameCount);
        TransitionSlowFrameSample slowFrame = Assert.Single(snapshot.SlowFrameSamples);
        Assert.Equal(61, slowFrame.DurationMilliseconds, precision: 6);
        Assert.Equal(0.75, slowFrame.VideoPositionSeconds);
        Assert.Equal(1, snapshot.FinalizedResourceCount);
        Assert.Equal(2, snapshot.DeferredGcAtMilliseconds.Count);
        Assert.Equal(new TransitionGcCounts(3, 1, 1), snapshot.NaturalGcDelta);
        Assert.Same(
            snapshot,
            trace.Complete(
                TransitionCompletionStatus.Succeeded,
                new TransitionGcCounts(20, 20, 20),
                TransitionGcFlushResult.None));

        string message = snapshot.ToLogMessage();
        Assert.Contains("kind=saveload", message, StringComparison.Ordinal);
        Assert.Contains("status=Cancelled", message, StringComparison.Ordinal);
        Assert.Contains("slow_frames=1/1/1", message, StringComparison.Ordinal);
        Assert.Contains("slow_frame_samples=", message, StringComparison.Ordinal);
        Assert.Contains("@0.750", message, StringComparison.Ordinal);
        Assert.Contains("asset_load_limit=8", message, StringComparison.Ordinal);
        Assert.Contains("gc_flush=optimized_nonblocking_ok", message, StringComparison.Ordinal);
    }

    [Fact]
    public void PerformanceSnapshotReportsCompletedNoGcRegion()
    {
        var counts = new TransitionGcCounts(2, 1, 0);
        var trace = new TransitionPerformanceTrace(61, TransitionInvocationKind.Embark, counts);
        var start = new TransitionNoGcRegionStartResult(
            61,
            256L * 1024 * 1024,
            counts,
            Attempted: true,
            Started: true,
            Inherited: false,
            RequestMilliseconds: 0.5,
            ErrorType: null);
        var completion = new TransitionNoGcRegionCompletion(
            IsCurrentSession: true,
            start,
            EndAttempted: true,
            EndSucceeded: true,
            CollectionObserved: false,
            EndMilliseconds: 0.25,
            EndErrorType: null);

        TransitionPerformanceSnapshot snapshot = trace.Complete(
            TransitionCompletionStatus.Succeeded,
            counts,
            TransitionGcFlushResult.None,
            completion);

        Assert.Contains("no_gc=completed:256MiB", snapshot.ToLogMessage(), StringComparison.Ordinal);
        Assert.Contains("managed_alloc=", snapshot.ToLogMessage(), StringComparison.Ordinal);
    }
}
