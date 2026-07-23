using MegaCrit.Sts2.Core.Nodes;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.Transition;

internal sealed class NinjaSlayerTransitionSession : IDisposable
{
    private static long _nextSessionId;
    private readonly CancellationToken _externalCancellation;
    private readonly CancellationTokenSource _lifetime;
    private readonly TransitionCompletionProtocol _protocol;
    private readonly ITransitionViewAdapter _view;
    private Task _animationTask = Task.CompletedTask;
    private int _loadSmoothingStarted;
    private int _disposed;

    public NinjaSlayerTransitionSession(ITransitionViewAdapter view, CancellationToken externalCancellation)
    {
        _view = view;
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

    public NinjaSlayerTransitionOverlay PrepareAnimatedView() => _view.PrepareAnimated();

    public void HoldBackdrop() => _view.HoldBackdrop();

    public void BeginLoadSmoothing()
    {
        if (Interlocked.CompareExchange(ref _loadSmoothingStarted, 1, 0) == 0)
        {
            NinjaSlayerTransitionLoadSmoothing.BeginAnimation();
        }
    }

    public void EndLoadSmoothing()
    {
        if (Interlocked.CompareExchange(ref _loadSmoothingStarted, 0, 1) == 1)
        {
            NinjaSlayerTransitionLoadSmoothing.EndAnimationAndCollectDeferred();
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
        CaptureCleanup(cleanupFailures, _lifetime.Cancel);
        CaptureCleanup(cleanupFailures, _view.StopPlayback);
        CaptureCleanup(cleanupFailures, EndLoadSmoothing);
        CaptureCleanup(cleanupFailures, () => RestoreTransition(forceRelease));

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

        var result = new TransitionCompletionResult(SessionId, status, diagnostic);
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
