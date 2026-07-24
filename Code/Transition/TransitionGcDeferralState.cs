using System.Diagnostics;

namespace NinjaSlayer.Code.Transition;

internal readonly record struct TransitionGcDeferralCompletion(
    bool IsCurrentSession,
    long SessionId,
    int DeferredRequestCount);

internal sealed class TransitionGcDeferralState
{
    private readonly object _sync = new();
    private long _activeSessionId;
    private int _deferredRequestCount;

    public bool IsActive
    {
        get
        {
            lock (_sync)
            {
                return _activeSessionId != 0;
            }
        }
    }

    public int Begin(long sessionId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionId);

        lock (_sync)
        {
            if (_activeSessionId == sessionId)
            {
                return 0;
            }

            int carriedRequestCount = _activeSessionId == 0 ? 0 : _deferredRequestCount;
            _activeSessionId = sessionId;
            _deferredRequestCount = carriedRequestCount;
            return carriedRequestCount;
        }
    }

    public bool TryDefer(out long sessionId, out int requestOrdinal)
    {
        lock (_sync)
        {
            if (_activeSessionId == 0)
            {
                sessionId = 0;
                requestOrdinal = 0;
                return false;
            }

            sessionId = _activeSessionId;
            requestOrdinal = ++_deferredRequestCount;
            return true;
        }
    }

    public TransitionGcDeferralCompletion Complete(long sessionId)
    {
        lock (_sync)
        {
            if (_activeSessionId != sessionId)
            {
                return new TransitionGcDeferralCompletion(false, sessionId, 0);
            }

            int deferredRequestCount = _deferredRequestCount;
            _activeSessionId = 0;
            _deferredRequestCount = 0;
            return new TransitionGcDeferralCompletion(true, sessionId, deferredRequestCount);
        }
    }
}

internal readonly record struct TransitionGcFlushResult(
    int DeferredRequestCount,
    bool Attempted,
    bool Succeeded,
    double RequestMilliseconds,
    string? ErrorType)
{
    public static TransitionGcFlushResult None { get; } = new(0, false, false, 0, null);
}

internal static class TransitionGcRequestExecutor
{
    public static TransitionGcFlushResult Execute(int deferredRequestCount, Action requestCollection)
    {
        ArgumentNullException.ThrowIfNull(requestCollection);
        if (deferredRequestCount <= 0)
        {
            return TransitionGcFlushResult.None;
        }

        long startedAt = Stopwatch.GetTimestamp();
        try
        {
            requestCollection();
            return new TransitionGcFlushResult(
                deferredRequestCount,
                Attempted: true,
                Succeeded: true,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                ErrorType: null);
        }
        catch (Exception ex)
        {
            return new TransitionGcFlushResult(
                deferredRequestCount,
                Attempted: true,
                Succeeded: false,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                ex.GetType().Name);
        }
    }
}
