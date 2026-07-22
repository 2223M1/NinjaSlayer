using System.Diagnostics;

namespace NinjaSlayer.Code.Combat;

internal enum MemoSearchLookup
{
    NewState,
    Cached,
    BudgetExceeded
}

internal sealed class BoundedMemoSearch<TKey, TResult>(int maximumStates, TimeSpan maximumTime)
    where TKey : notnull
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly Dictionary<TKey, TResult> _results = [];
    private int _visitedStates;

    public int VisitedStates => _visitedStates;

    public MemoSearchLookup Lookup(TKey key, out TResult result)
    {
        if (_results.TryGetValue(key, out result!))
        {
            return MemoSearchLookup.Cached;
        }

        if (_visitedStates >= maximumStates || _stopwatch.Elapsed >= maximumTime)
        {
            result = default!;
            return MemoSearchLookup.BudgetExceeded;
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
