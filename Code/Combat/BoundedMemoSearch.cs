using System.Diagnostics;

namespace NinjaSlayer.Code.Combat;

internal enum MemoSearchLookup
{
    NewState,
    Cached,
    StateBudgetExceeded,
    WatchdogExpired
}

internal sealed class BoundedMemoSearch<TKey, TResult>
    where TKey : notnull
{
    private readonly int _maximumStates;
    private readonly TimeSpan _maximumTime;
    private readonly Stopwatch _stopwatch;
    private readonly Func<TimeSpan> _elapsed;
    private readonly Dictionary<TKey, TResult> _results = [];
    private int _visitedStates;

    public BoundedMemoSearch(
        int maximumStates,
        TimeSpan maximumTime,
        Func<TimeSpan>? elapsed = null)
    {
        _maximumStates = Math.Max(0, maximumStates);
        _maximumTime = maximumTime < TimeSpan.Zero ? TimeSpan.Zero : maximumTime;
        _stopwatch = elapsed is null ? Stopwatch.StartNew() : new Stopwatch();
        _elapsed = elapsed ?? (() => _stopwatch.Elapsed);
    }

    public int VisitedStates => _visitedStates;

    public MemoSearchLookup Lookup(TKey key, out TResult result)
    {
        if (_results.TryGetValue(key, out result!))
        {
            return MemoSearchLookup.Cached;
        }

        // The state count is deterministic and therefore always wins if both limits are reached.
        if (_visitedStates >= _maximumStates)
        {
            result = default!;
            return MemoSearchLookup.StateBudgetExceeded;
        }

        if (_elapsed() >= _maximumTime)
        {
            result = default!;
            return MemoSearchLookup.WatchdogExpired;
        }

        _visitedStates++;
        result = default!;
        return MemoSearchLookup.NewState;
    }

    public void Store(TKey key, TResult result)
    {
        _results[key] = result;
    }
}
