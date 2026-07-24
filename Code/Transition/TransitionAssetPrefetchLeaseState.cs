namespace NinjaSlayer.Code.Transition;

internal enum TransitionAssetPrefetchPhase
{
    Idle,
    Preparing,
    Claimed
}

internal readonly record struct TransitionAssetPrefetchSnapshot(
    long Generation,
    TransitionAssetPrefetchPhase Phase,
    int ProtectedPathCount);

internal sealed class TransitionAssetPrefetchLeaseState
{
    private readonly object _syncRoot = new();
    private readonly HashSet<string> _protectedPaths = new(StringComparer.Ordinal);
    private long _generation;
    private TransitionAssetPrefetchPhase _phase;

    public long BeginOrExtend(IEnumerable<string> paths)
    {
        string[] snapshot = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (snapshot.Length == 0)
        {
            return 0;
        }

        lock (_syncRoot)
        {
            if (_phase == TransitionAssetPrefetchPhase.Claimed)
            {
                return 0;
            }

            if (_phase == TransitionAssetPrefetchPhase.Idle)
            {
                _generation++;
                _protectedPaths.Clear();
                _phase = TransitionAssetPrefetchPhase.Preparing;
            }

            _protectedPaths.UnionWith(snapshot);
            return _generation;
        }
    }

    public long Claim()
    {
        lock (_syncRoot)
        {
            if (_phase == TransitionAssetPrefetchPhase.Idle)
            {
                return 0;
            }

            _phase = TransitionAssetPrefetchPhase.Claimed;
            return _generation;
        }
    }

    public string[] FilterUnprotected(IEnumerable<string> paths, out int protectedCount)
    {
        lock (_syncRoot)
        {
            if (_phase == TransitionAssetPrefetchPhase.Idle || _protectedPaths.Count == 0)
            {
                protectedCount = 0;
                return paths.ToArray();
            }

            string[] candidates = paths.ToArray();
            string[] result = candidates
                .Where(path => !_protectedPaths.Contains(path))
                .ToArray();
            protectedCount = candidates.Length - result.Length;
            return result;
        }
    }

    public bool TryRelease(long generation)
    {
        lock (_syncRoot)
        {
            if (_phase != TransitionAssetPrefetchPhase.Claimed || generation != _generation)
            {
                return false;
            }

            Reset();
            return true;
        }
    }

    public bool CancelUnclaimed()
    {
        lock (_syncRoot)
        {
            if (_phase != TransitionAssetPrefetchPhase.Preparing)
            {
                return false;
            }

            Reset();
            return true;
        }
    }

    public TransitionAssetPrefetchSnapshot Snapshot()
    {
        lock (_syncRoot)
        {
            return new TransitionAssetPrefetchSnapshot(_generation, _phase, _protectedPaths.Count);
        }
    }

    private void Reset()
    {
        _protectedPaths.Clear();
        _phase = TransitionAssetPrefetchPhase.Idle;
    }
}
