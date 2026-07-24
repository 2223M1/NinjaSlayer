using NinjaSlayer.Code.Transition;

namespace NinjaSlayer.LogicTests;

public sealed class TransitionLoadSmoothingTests
{
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
        Assert.Equal(1, snapshot.SessionFrames.FrameCount);
        Assert.Equal(1, snapshot.VideoFrames.FrameCount);
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
        Assert.Contains("slow_frames=0/0/0", message, StringComparison.Ordinal);
        Assert.Contains("gc_flush=optimized_nonblocking_ok", message, StringComparison.Ordinal);
    }
}
