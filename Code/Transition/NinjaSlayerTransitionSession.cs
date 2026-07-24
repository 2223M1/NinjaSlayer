using MegaCrit.Sts2.Core.Nodes;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Code.Diagnostics;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.Transition;

internal sealed class NinjaSlayerTransitionSession : IDisposable
{
    private static long _nextSessionId;
    private readonly CancellationToken _externalCancellation;
    private readonly CancellationTokenSource _lifetime;
    private readonly TransitionCompletionProtocol _protocol;
    private readonly ITransitionViewAdapter _view;
    private readonly TransitionInvocationKind _invocationKind;
    private Task _animationTask = Task.CompletedTask;
    private int _loadSmoothingStarted;
    private int _animationSmoothingEnded;
    private int _loadSmoothingCompleted;
    private int _disposed;
    private TransitionPerformanceTrace? _performanceTrace;

    public NinjaSlayerTransitionSession(
        ITransitionViewAdapter view,
        TransitionInvocationKind invocationKind,
        CancellationToken externalCancellation)
    {
        _view = view;
        _invocationKind = invocationKind;
        _externalCancellation = externalCancellation;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(externalCancellation);
        _protocol = new TransitionCompletionProtocol(Interlocked.Increment(ref _nextSessionId));
    }

    public NTransition Transition => _view.Transition;
    public long SessionId => _protocol.SessionId;
    public Task<TransitionCompletionResult> Completion => _protocol.Completion;
    public bool ShouldHoldBackdrop => !_protocol.IsCompletionStarted;

    public void Start(Func<NinjaSlayerTransitionSession, CancellationToken, Task> animationFactory)
    {
        if (!_protocol.TryStart())
        {
            throw new InvalidOperationException($"Transition session {SessionId} was already started.");
        }
        if (_externalCancellation.IsCancellationRequested)
        {
            throw new OperationCanceledException(_externalCancellation);
        }

        _animationTask = animationFactory(this, _lifetime.Token)
            ?? throw new InvalidOperationException("Transition animation factory returned null.");
        _ = ObserveAnimationAsync();
        _ = RunWatchdogAsync();
    }

    public bool TryClaimReveal() => _protocol.TryClaimReveal();

    public void PrepareInstantView() => _view.PrepareInstant();

    public NinjaSlayerTransitionOverlay PrepareAnimatedView() => _view.PrepareAnimated(_performanceTrace);

    public void HoldBackdrop() => _view.HoldBackdrop();

    public void BeginLoadSmoothing()
    {
        if (Interlocked.CompareExchange(ref _loadSmoothingStarted, 1, 0) == 0)
        {
            _performanceTrace = NinjaSlayerTransitionLoadSmoothing.BeginSession(SessionId, _invocationKind);
        }
    }

    public void EndAnimationSmoothing()
    {
        if (Volatile.Read(ref _loadSmoothingStarted) != 0
            && Interlocked.CompareExchange(ref _animationSmoothingEnded, 1, 0) == 0)
        {
            NinjaSlayerTransitionLoadSmoothing.EndAnimation(SessionId);
        }
    }

    public async Task WaitForAnimationAsync()
    {
        try
        {
            await _animationTask;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"NinjaSlayer transition animation faulted before reveal: {ex}");
        }
    }

    public Task<TransitionCompletionResult> CompleteAsync(
        TransitionCompletionStatus status,
        bool forceRelease,
        string? diagnostic = null)
    {
        if (!_protocol.TryBeginCompletion())
        {
            return Completion;
        }

        var cleanupFailures = new List<Exception>();
        TransitionPerformanceTrace? performanceTrace = _performanceTrace;
        TransitionGcCounts endingGcCounts = TransitionGcCounts.Capture();
        TransitionGcFlushResult gcFlush = TransitionGcFlushResult.None;
        CaptureCleanup(cleanupFailures, _lifetime.Cancel);
        CaptureCleanup(cleanupFailures, _view.StopPlayback);
        CaptureCleanup(cleanupFailures, EndAnimationSmoothing);
        CaptureCleanup(cleanupFailures, () => RestoreTransition(forceRelease));
        if (performanceTrace is not null)
        {
            CaptureCleanup(cleanupFailures, () => _view.DetachPerformanceTrace(performanceTrace));
        }

        CaptureCleanup(cleanupFailures, () =>
        {
            endingGcCounts = TransitionGcCounts.Capture();
            gcFlush = CompleteLoadSmoothing();
        });

        if (cleanupFailures.Count > 0)
        {
            status = TransitionCompletionStatus.Faulted;
            string cleanupDiagnostic = string.Join(" | ", cleanupFailures.Select(error => error.Message));
            diagnostic = string.IsNullOrWhiteSpace(diagnostic)
                ? cleanupDiagnostic
                : $"{diagnostic} | cleanup: {cleanupDiagnostic}";
            Entry.Logger.Error(
                $"NinjaSlayer transition session {SessionId} cleanup failed: " +
                string.Join(System.Environment.NewLine, cleanupFailures));
        }

        if (performanceTrace is not null)
        {
            TransitionPerformanceSnapshot snapshot = performanceTrace.Complete(status, endingGcCounts, gcFlush);
            Entry.Logger.Info(snapshot.ToLogMessage());
            if (gcFlush.Attempted && !gcFlush.Succeeded)
            {
                Entry.Logger.Warn(
                    $"NinjaSlayer transition session {SessionId} could not request optimized non-blocking GC " +
                    $"({gcFlush.ErrorType ?? "unknown"}); natural GC will reclaim the deferred assets.");
            }
        }

        var result = new TransitionCompletionResult(SessionId, status, diagnostic);
        NinjaSlayerRuntimeCounters.RecordTransition(result.Status);
        _protocol.Finish(result);
        NinjaSlayerTransitionGate.OnSessionCompleted(this);
        Dispose();
        return Completion;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _lifetime.Dispose();
    }

    private async Task ObserveAnimationAsync()
    {
        try
        {
            await _animationTask;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            if (_externalCancellation.IsCancellationRequested)
            {
                await CompleteAsync(
                    TransitionCompletionStatus.Cancelled,
                    forceRelease: true,
                    "Transition cancellation was requested.");
            }
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"NinjaSlayer transition animation failed: {ex}");
            await CompleteAsync(TransitionCompletionStatus.Faulted, forceRelease: true, ex.ToString());
        }
    }

    private async Task RunWatchdogAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), _lifetime.Token);
            Entry.Logger.Error("NinjaSlayer transition exceeded 30 seconds; forcing input and screen release.");
            await CompleteAsync(
                TransitionCompletionStatus.TimedOut,
                forceRelease: true,
                "Transition exceeded the 30 second watchdog.");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            if (_externalCancellation.IsCancellationRequested)
            {
                await CompleteAsync(
                    TransitionCompletionStatus.Cancelled,
                    forceRelease: true,
                    "Transition cancellation was requested.");
            }
        }
        catch (Exception ex)
        {
            Entry.Logger.Error($"NinjaSlayer transition watchdog failed: {ex}");
            await CompleteAsync(TransitionCompletionStatus.Faulted, forceRelease: true, ex.ToString());
        }
    }

    private void RestoreTransition(bool forceRelease)
    {
        _view.Restore(forceRelease);
    }

    private TransitionGcFlushResult CompleteLoadSmoothing()
    {
        if (Volatile.Read(ref _loadSmoothingStarted) == 0
            || Interlocked.CompareExchange(ref _loadSmoothingCompleted, 1, 0) != 0)
        {
            return TransitionGcFlushResult.None;
        }

        return NinjaSlayerTransitionLoadSmoothing.CompleteSession(SessionId);
    }

    private static void CaptureCleanup(ICollection<Exception> failures, Action cleanup)
    {
        try
        {
            cleanup();
        }
        catch (Exception ex)
        {
            failures.Add(ex);
        }
    }
}
