namespace NinjaSlayer.Code.ExternalAnimations;

internal enum FinisherSessionPhase
{
    Created,
    Intercepting,
    AwaitingPostCard,
    Committing,
    Restoring,
    Finished
}

internal enum FinisherCompletionStatus
{
    Succeeded,
    Degraded,
    Faulted,
    Cancelled
}

internal enum FinisherCompletionMode
{
    PlayPose,
    CommitWithoutPose,
    ReleaseOnly
}

internal sealed record FinisherCompletionResult(
    long SessionId,
    FinisherCompletionStatus Status,
    FinisherCompletionMode Mode,
    string? Diagnostic);

internal sealed class FinisherCompletionProtocol(long sessionId)
{
    private readonly object _sync = new();
    private readonly TaskCompletionSource<FinisherCompletionResult> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _completionStarted;
    private FinisherSessionPhase _phase = FinisherSessionPhase.Created;

    public long SessionId { get; } = sessionId;
    public Task<FinisherCompletionResult> Completion => _completion.Task;

    public FinisherSessionPhase Phase
    {
        get
        {
            lock (_sync)
            {
                return _phase;
            }
        }
    }

    public bool TryStart() => TryTransition(FinisherSessionPhase.Intercepting);

    public bool TryAwaitPostCard() => TryTransition(FinisherSessionPhase.AwaitingPostCard);

    public bool TryBeginCompletion() => Interlocked.CompareExchange(ref _completionStarted, 1, 0) == 0;

    public bool TryTransition(FinisherSessionPhase next)
    {
        lock (_sync)
        {
            if (!CanTransition(_phase, next))
            {
                return false;
            }

            _phase = next;
            return true;
        }
    }

    public void Finish(FinisherCompletionResult result)
    {
        lock (_sync)
        {
            if (_phase != FinisherSessionPhase.Finished)
            {
                _phase = FinisherSessionPhase.Finished;
            }
        }

        _completion.TrySetResult(result);
    }

    private static bool CanTransition(FinisherSessionPhase current, FinisherSessionPhase next) =>
        (current, next) switch
        {
            (FinisherSessionPhase.Created, FinisherSessionPhase.Intercepting) => true,
            (FinisherSessionPhase.Created, FinisherSessionPhase.Restoring) => true,
            (FinisherSessionPhase.Intercepting, FinisherSessionPhase.AwaitingPostCard) => true,
            (FinisherSessionPhase.Intercepting, FinisherSessionPhase.Committing) => true,
            (FinisherSessionPhase.Intercepting, FinisherSessionPhase.Restoring) => true,
            (FinisherSessionPhase.AwaitingPostCard, FinisherSessionPhase.Committing) => true,
            (FinisherSessionPhase.AwaitingPostCard, FinisherSessionPhase.Restoring) => true,
            (FinisherSessionPhase.Committing, FinisherSessionPhase.Restoring) => true,
            (FinisherSessionPhase.Restoring, FinisherSessionPhase.Finished) => true,
            _ => false
        };
}

internal sealed class FinisherProtectionProtocol(
    long sessionId,
    long combatEpoch,
    long protectionSequence,
    int hpBefore,
    bool temporaryHpBumpApplied)
{
    private const int Active = 0;
    private const int Confirmed = 1;
    private const int Released = 2;
    private int _state;

    public long SessionId { get; } = sessionId;
    public long CombatEpoch { get; } = combatEpoch;
    public long ProtectionSequence { get; } = protectionSequence;
    public int HpBefore { get; } = hpBefore;
    public bool TemporaryHpBumpApplied { get; } = temporaryHpBumpApplied;
    public bool IsConfirmed => Volatile.Read(ref _state) == Confirmed;
    public bool IsReleased => Volatile.Read(ref _state) == Released;

    public bool TryConfirm() => Interlocked.CompareExchange(ref _state, Confirmed, Active) == Active;

    public bool TryReleaseAndShouldRollback(
        long currentSessionId,
        long currentCombatEpoch,
        int currentHp,
        bool contextIsCurrent)
    {
        if (Interlocked.CompareExchange(ref _state, Released, Active) != Active)
        {
            return false;
        }

        return contextIsCurrent
            && SessionId == currentSessionId
            && CombatEpoch == currentCombatEpoch
            && TemporaryHpBumpApplied
            && HpBefore == 1
            && currentHp == 2;
    }
}

internal sealed class FinisherCleanupAccumulator
{
    private readonly List<Exception> _failures = [];

    public int FailureCount => _failures.Count;

    public void Capture(Action cleanup)
    {
        try
        {
            cleanup();
        }
        catch (Exception ex)
        {
            _failures.Add(ex);
        }
    }

    public async Task CaptureAsync(Func<Task> cleanup)
    {
        try
        {
            await cleanup();
        }
        catch (Exception ex)
        {
            _failures.Add(ex);
        }
    }

    public void ThrowIfAny(string message)
    {
        if (_failures.Count > 0)
        {
            throw new AggregateException(message, _failures);
        }
    }
}
