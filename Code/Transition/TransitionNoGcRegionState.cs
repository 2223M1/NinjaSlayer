using System.Runtime;

namespace NinjaSlayer.Code.Transition;

internal readonly record struct TransitionGcCounts(int Generation0, int Generation1, int Generation2)
{
    public static TransitionGcCounts Capture() => new(
        GC.CollectionCount(0),
        GC.CollectionCount(1),
        GC.CollectionCount(2));
}

internal sealed class TransitionNoGcRegionState
{
    private readonly Lock _lock = new();
    private long _sessionId;
    private bool _started;
    private TransitionGcCounts _baseline;

    public void Begin(
        long sessionId,
        Func<bool> tryStart,
        Func<TransitionGcCounts> captureGcCounts)
    {
        lock (_lock)
        {
            if (_sessionId == sessionId)
            {
                return;
            }

            if (_sessionId != 0 && _started)
            {
                _sessionId = sessionId;
                return;
            }

            _sessionId = sessionId;
            _started = false;
            try
            {
                if (tryStart())
                {
                    _baseline = captureGcCounts();
                    _started = true;
                }
            }
            catch
            {
                _baseline = default;
            }
        }
    }

    public void Complete(
        long sessionId,
        TransitionGcCounts endingGcCounts,
        Func<bool> isRegionActive,
        Action endRegion)
    {
        bool shouldEnd;
        lock (_lock)
        {
            if (_sessionId != sessionId)
            {
                return;
            }

            shouldEnd = _started && endingGcCounts == _baseline;
            _sessionId = 0;
            _started = false;
            _baseline = default;
        }

        if (!shouldEnd)
        {
            return;
        }

        try
        {
            if (isRegionActive())
            {
                endRegion();
            }
        }
        catch
        {
            // A collection or another owner may have already ended the region.
        }
    }

    public static bool IsRuntimeRegionActive() =>
        GCSettings.LatencyMode == GCLatencyMode.NoGCRegion;
}
