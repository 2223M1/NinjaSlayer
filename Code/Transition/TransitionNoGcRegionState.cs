using System.Diagnostics;
using System.Runtime;

namespace NinjaSlayer.Code.Transition;

internal readonly record struct TransitionNoGcRegionStartResult(
    long SessionId,
    long BudgetBytes,
    TransitionGcCounts StartingGcCounts,
    bool Attempted,
    bool Started,
    bool Inherited,
    double RequestMilliseconds,
    string? ErrorType)
{
    public static TransitionNoGcRegionStartResult None { get; } = new(
        0,
        0,
        default,
        Attempted: false,
        Started: false,
        Inherited: false,
        RequestMilliseconds: 0,
        ErrorType: null);
}

internal readonly record struct TransitionNoGcRegionCompletion(
    bool IsCurrentSession,
    TransitionNoGcRegionStartResult Start,
    bool EndAttempted,
    bool EndSucceeded,
    bool CollectionObserved,
    double EndMilliseconds,
    string? EndErrorType)
{
    public static TransitionNoGcRegionCompletion None { get; } = new(
        IsCurrentSession: false,
        TransitionNoGcRegionStartResult.None,
        EndAttempted: false,
        EndSucceeded: false,
        CollectionObserved: false,
        EndMilliseconds: 0,
        EndErrorType: null);
}

internal sealed class TransitionNoGcRegionState
{
    private readonly object _sync = new();
    private long _activeSessionId;
    private TransitionNoGcRegionStartResult _start;

    public TransitionNoGcRegionStartResult Begin(
        long sessionId,
        long budgetBytes,
        TransitionGcCounts startingGcCounts,
        Func<bool> tryStart,
        Func<TransitionGcCounts> captureGcCounts)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(budgetBytes);
        ArgumentNullException.ThrowIfNull(tryStart);
        ArgumentNullException.ThrowIfNull(captureGcCounts);

        lock (_sync)
        {
            if (_activeSessionId == sessionId)
            {
                return _start;
            }

            if (_activeSessionId != 0 && _start.Started)
            {
                _activeSessionId = sessionId;
                _start = _start with
                {
                    SessionId = sessionId,
                    Inherited = true
                };
                return _start;
            }

            _activeSessionId = sessionId;
            _start = TransitionNoGcRegionExecutor.TryStart(
                sessionId,
                budgetBytes,
                startingGcCounts,
                tryStart,
                captureGcCounts);
            return _start;
        }
    }

    public TransitionNoGcRegionCompletion Complete(
        long sessionId,
        TransitionGcCounts endingGcCounts,
        Func<bool> isRegionActive,
        Action endRegion)
    {
        ArgumentNullException.ThrowIfNull(isRegionActive);
        ArgumentNullException.ThrowIfNull(endRegion);

        lock (_sync)
        {
            if (_activeSessionId != sessionId)
            {
                return TransitionNoGcRegionCompletion.None;
            }

            TransitionNoGcRegionStartResult start = _start;
            _activeSessionId = 0;
            _start = TransitionNoGcRegionStartResult.None;
            return TransitionNoGcRegionExecutor.Complete(
                start,
                endingGcCounts,
                isRegionActive,
                endRegion);
        }
    }
}

internal static class TransitionNoGcRegionExecutor
{
    public static TransitionNoGcRegionStartResult TryStart(
        long sessionId,
        long budgetBytes,
        TransitionGcCounts startingGcCounts,
        Func<bool> tryStart,
        Func<TransitionGcCounts> captureGcCounts)
    {
        long startedAt = Stopwatch.GetTimestamp();
        try
        {
            bool started = tryStart();
            // TryStartNoGCRegion may perform a full collection before the region becomes active.
            // Only collections after it returns belong to the protected transition interval.
            TransitionGcCounts protectedIntervalCounts = captureGcCounts();
            return new TransitionNoGcRegionStartResult(
                sessionId,
                budgetBytes,
                protectedIntervalCounts,
                Attempted: true,
                Started: started,
                Inherited: false,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                ErrorType: started ? null : "Rejected");
        }
        catch (Exception ex)
        {
            TransitionGcCounts failedAttemptCounts = CaptureOrFallback(
                captureGcCounts,
                startingGcCounts);
            return new TransitionNoGcRegionStartResult(
                sessionId,
                budgetBytes,
                failedAttemptCounts,
                Attempted: true,
                Started: false,
                Inherited: false,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                ex.GetType().Name);
        }
    }

    private static TransitionGcCounts CaptureOrFallback(
        Func<TransitionGcCounts> captureGcCounts,
        TransitionGcCounts fallback)
    {
        try
        {
            return captureGcCounts();
        }
        catch
        {
            return fallback;
        }
    }

    public static TransitionNoGcRegionCompletion Complete(
        TransitionNoGcRegionStartResult start,
        TransitionGcCounts endingGcCounts,
        Func<bool> isRegionActive,
        Action endRegion)
    {
        if (!start.Started)
        {
            return new TransitionNoGcRegionCompletion(
                IsCurrentSession: true,
                start,
                EndAttempted: false,
                EndSucceeded: false,
                CollectionObserved: false,
                EndMilliseconds: 0,
                EndErrorType: null);
        }

        bool collectionObserved = endingGcCounts != start.StartingGcCounts;
        if (collectionObserved)
        {
            return new TransitionNoGcRegionCompletion(
                IsCurrentSession: true,
                start,
                EndAttempted: false,
                EndSucceeded: false,
                CollectionObserved: true,
                EndMilliseconds: 0,
                EndErrorType: "CollectionObserved");
        }

        bool active;
        try
        {
            active = isRegionActive();
        }
        catch (Exception ex)
        {
            return new TransitionNoGcRegionCompletion(
                IsCurrentSession: true,
                start,
                EndAttempted: false,
                EndSucceeded: false,
                CollectionObserved: false,
                EndMilliseconds: 0,
                ex.GetType().Name);
        }

        if (!active)
        {
            return new TransitionNoGcRegionCompletion(
                IsCurrentSession: true,
                start,
                EndAttempted: false,
                EndSucceeded: false,
                CollectionObserved: false,
                EndMilliseconds: 0,
                EndErrorType: "RegionNotActive");
        }

        long startedAt = Stopwatch.GetTimestamp();
        try
        {
            endRegion();
            return new TransitionNoGcRegionCompletion(
                IsCurrentSession: true,
                start,
                EndAttempted: true,
                EndSucceeded: true,
                CollectionObserved: false,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                EndErrorType: null);
        }
        catch (Exception ex)
        {
            return new TransitionNoGcRegionCompletion(
                IsCurrentSession: true,
                start,
                EndAttempted: true,
                EndSucceeded: false,
                CollectionObserved: false,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                ex.GetType().Name);
        }
    }

    public static bool IsRuntimeRegionActive() =>
        GCSettings.LatencyMode == GCLatencyMode.NoGCRegion;
}
