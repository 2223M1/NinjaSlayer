namespace NinjaSlayer.Code.Transition;

internal enum TransitionCompletionStatus
{
    Succeeded,
    Faulted,
    Cancelled,
    TimedOut,
    Superseded
}

internal sealed record TransitionCompletionResult(
    long SessionId,
    TransitionCompletionStatus Status,
    string? Diagnostic);

internal sealed class TransitionCompletionProtocol(long sessionId)
{
    private readonly TaskCompletionSource<TransitionCompletionResult> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _started;
    private int _revealClaimed;
    private int _completionStarted;

    public long SessionId { get; } = sessionId;
    public Task<TransitionCompletionResult> Completion => _completion.Task;
    public bool IsCompletionStarted => Volatile.Read(ref _completionStarted) != 0;

    public bool TryStart() => Interlocked.CompareExchange(ref _started, 1, 0) == 0;

    public bool TryClaimReveal() =>
        Volatile.Read(ref _started) != 0
        && Volatile.Read(ref _completionStarted) == 0
        && Interlocked.CompareExchange(ref _revealClaimed, 1, 0) == 0;

    public bool TryBeginCompletion() =>
        Interlocked.CompareExchange(ref _completionStarted, 1, 0) == 0;

    public void Finish(TransitionCompletionResult result) => _completion.TrySetResult(result);
}
