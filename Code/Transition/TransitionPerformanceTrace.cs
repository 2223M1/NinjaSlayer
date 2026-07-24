using System.Diagnostics;
using System.Globalization;

namespace NinjaSlayer.Code.Transition;

internal enum TransitionInvocationKind
{
    Embark,
    SaveLoad
}

internal readonly record struct TransitionGcCounts(int Generation0, int Generation1, int Generation2)
{
    public static TransitionGcCounts Capture() => new(
        GC.CollectionCount(0),
        GC.CollectionCount(1),
        GC.CollectionCount(2));
}

internal readonly record struct TransitionFrameMetricsSnapshot(
    int FrameCount,
    double LongestFrameMilliseconds,
    int Over25Milliseconds,
    int Over40Milliseconds,
    int Over60Milliseconds);

internal readonly record struct TransitionSlowFrameSample(
    double AtMilliseconds,
    double DurationMilliseconds,
    double? VideoPositionSeconds);

internal readonly record struct TransitionPhaseSample(
    string Name,
    double AtMilliseconds,
    double DurationMilliseconds);

internal sealed class TransitionFrameMetrics
{
    private readonly object _sync = new();
    private int _frameCount;
    private double _longestFrameMilliseconds;
    private int _over25Milliseconds;
    private int _over40Milliseconds;
    private int _over60Milliseconds;

    public void Record(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0)
        {
            return;
        }

        double milliseconds = deltaSeconds * 1000;
        lock (_sync)
        {
            _frameCount++;
            _longestFrameMilliseconds = Math.Max(_longestFrameMilliseconds, milliseconds);
            if (milliseconds > 25)
            {
                _over25Milliseconds++;
            }
            if (milliseconds > 40)
            {
                _over40Milliseconds++;
            }
            if (milliseconds > 60)
            {
                _over60Milliseconds++;
            }
        }
    }

    public TransitionFrameMetricsSnapshot Snapshot()
    {
        lock (_sync)
        {
            return new TransitionFrameMetricsSnapshot(
                _frameCount,
                _longestFrameMilliseconds,
                _over25Milliseconds,
                _over40Milliseconds,
                _over60Milliseconds);
        }
    }
}

internal sealed record TransitionPerformanceSnapshot(
    long SessionId,
    TransitionInvocationKind Kind,
    TransitionCompletionStatus Status,
    double TotalMilliseconds,
    double VideoMilliseconds,
    double StreamAcquireMilliseconds,
    double PlayCallMilliseconds,
    double? FirstPostPlayFrameMilliseconds,
    TransitionFrameMetricsSnapshot SessionFrames,
    TransitionFrameMetricsSnapshot VideoFrames,
    IReadOnlyList<TransitionSlowFrameSample> SlowFrameSamples,
    IReadOnlyList<TransitionPhaseSample> PhaseSamples,
    int FinalizedResourceCount,
    int FinalizeBatchCount,
    double LongestFinalizeBatchMilliseconds,
    IReadOnlyList<double> DeferredGcAtMilliseconds,
    TransitionGcCounts NaturalGcDelta,
    TransitionGcFlushResult GcFlush,
    TransitionNoGcRegionCompletion NoGcRegion,
    long ManagedAllocatedBytes)
{
    public string ToLogMessage()
    {
        string firstFrame = FirstPostPlayFrameMilliseconds is { } firstFrameMilliseconds
            ? $"{firstFrameMilliseconds.ToString("F2", CultureInfo.InvariantCulture)}ms"
            : "n/a";
        string gcAt = DeferredGcAtMilliseconds.Count == 0
            ? "none"
            : string.Join('/', DeferredGcAtMilliseconds.Select(value => value.ToString("F0", CultureInfo.InvariantCulture)));
        string gcFlush = !GcFlush.Attempted
            ? "none"
            : GcFlush.Succeeded
                ? "optimized_nonblocking_ok"
                : $"optimized_nonblocking_failed:{GcFlush.ErrorType ?? "unknown"}";
        string noGcRegion = FormatNoGcRegion();
        string slowFrameSamples = SlowFrameSamples.Count == 0
            ? "none"
            : string.Join('/', SlowFrameSamples.Select(sample =>
            {
                string position = sample.VideoPositionSeconds is { } videoPosition
                    ? videoPosition.ToString("F3", CultureInfo.InvariantCulture)
                    : "n/a";
                return string.Concat(
                    sample.AtMilliseconds.ToString("F0", CultureInfo.InvariantCulture),
                    ':',
                    sample.DurationMilliseconds.ToString("F1", CultureInfo.InvariantCulture),
                    '@',
                    position);
            }));
        string phaseSamples = PhaseSamples.Count == 0
            ? "none"
            : string.Join('/', PhaseSamples.Select(sample => string.Concat(
                sample.Name,
                '@',
                sample.AtMilliseconds.ToString("F0", CultureInfo.InvariantCulture),
                '+',
                sample.DurationMilliseconds.ToString("F1", CultureInfo.InvariantCulture))));

        return string.Concat(
            FormattableString.Invariant($"NinjaSlayer transition performance: session={SessionId}, kind={Kind.ToString().ToLowerInvariant()}, status={Status}, total={TotalMilliseconds:F0}ms, video={VideoMilliseconds:F0}ms, "),
            FormattableString.Invariant($"stream={StreamAcquireMilliseconds:F2}ms, play={PlayCallMilliseconds:F2}ms, first_process_frame={firstFrame}, frames={SessionFrames.FrameCount}, "),
            FormattableString.Invariant($"longest_frame={SessionFrames.LongestFrameMilliseconds:F2}ms, slow_frames={SessionFrames.Over25Milliseconds}/{SessionFrames.Over40Milliseconds}/{SessionFrames.Over60Milliseconds}, "),
            FormattableString.Invariant($"video_frames={VideoFrames.FrameCount}, video_longest_frame={VideoFrames.LongestFrameMilliseconds:F2}ms, video_slow_frames={VideoFrames.Over25Milliseconds}/{VideoFrames.Over40Milliseconds}/{VideoFrames.Over60Milliseconds}, "),
            FormattableString.Invariant($"slow_frame_samples={slowFrameSamples}, phases={phaseSamples}, asset_load_limit={TransitionLoadConcurrencyPolicy.VisibleTransitionConcurrentLoadLimit}, "),
            FormattableString.Invariant($"finalized={FinalizedResourceCount}, batches={FinalizeBatchCount}, longest_batch={LongestFinalizeBatchMilliseconds:F2}ms, deferred_gc={GcFlush.DeferredRequestCount}, "),
            FormattableString.Invariant($"gc_at_ms={gcAt}, natural_gc={NaturalGcDelta.Generation0}/{NaturalGcDelta.Generation1}/{NaturalGcDelta.Generation2}, no_gc={noGcRegion}, managed_alloc={ManagedAllocatedBytes / 1048576.0:F1}MiB, "),
            FormattableString.Invariant($"gc_flush={gcFlush}, gc_request={GcFlush.RequestMilliseconds:F2}ms."));
    }

    private string FormatNoGcRegion()
    {
        if (!NoGcRegion.IsCurrentSession || !NoGcRegion.Start.Attempted)
        {
            return "none";
        }

        TransitionNoGcRegionStartResult start = NoGcRegion.Start;
        string budget = FormattableString.Invariant($"{start.BudgetBytes / 1048576.0:F0}MiB");
        string inherited = start.Inherited ? "inherited_" : string.Empty;
        if (!start.Started)
        {
            return FormattableString.Invariant(
                $"{inherited}start_failed:{start.ErrorType ?? "unknown"}@{start.RequestMilliseconds:F2}ms");
        }

        if (NoGcRegion.CollectionObserved)
        {
            return FormattableString.Invariant(
                $"{inherited}exhausted:{budget}@{start.RequestMilliseconds:F2}ms");
        }

        if (NoGcRegion.EndSucceeded)
        {
            return FormattableString.Invariant(
                $"{inherited}completed:{budget}@{start.RequestMilliseconds:F2}/{NoGcRegion.EndMilliseconds:F2}ms");
        }

        return FormattableString.Invariant(
            $"{inherited}end_failed:{NoGcRegion.EndErrorType ?? "unknown"}:{budget}@{start.RequestMilliseconds:F2}/{NoGcRegion.EndMilliseconds:F2}ms");
    }
}

internal sealed class TransitionPerformanceTrace
{
    private const int MaxSlowFrameSamples = 16;
    private const int MaxPhaseSamples = 24;
    private readonly object _sync = new();
    private readonly long _startedAt = Stopwatch.GetTimestamp();
    private readonly long _startingAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false);
    private readonly TransitionGcCounts _startingGcCounts;
    private readonly TransitionFrameMetrics _sessionFrames = new();
    private readonly TransitionFrameMetrics _videoFrames = new();
    private readonly List<double> _deferredGcAtMilliseconds = [];
    private readonly List<TransitionSlowFrameSample> _slowFrameSamples = [];
    private readonly List<TransitionPhaseSample> _phaseSamples = [];
    private long _videoStartedAt;
    private bool _videoActive;
    private double _videoMilliseconds;
    private double _streamAcquireMilliseconds;
    private double _playCallMilliseconds;
    private double? _firstPostPlayFrameMilliseconds;
    private int _finalizedResourceCount;
    private int _finalizeBatchCount;
    private double _longestFinalizeBatchMilliseconds;
    private TransitionPerformanceSnapshot? _completedSnapshot;

    public TransitionPerformanceTrace(
        long sessionId,
        TransitionInvocationKind kind,
        TransitionGcCounts? startingGcCounts = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionId);
        SessionId = sessionId;
        Kind = kind;
        _startingGcCounts = startingGcCounts ?? TransitionGcCounts.Capture();
    }

    public long SessionId { get; }
    public TransitionInvocationKind Kind { get; }

    public void RecordFrame(double deltaSeconds, double? videoPositionSeconds = null)
    {
        bool videoActive;
        lock (_sync)
        {
            if (_completedSnapshot is not null)
            {
                return;
            }

            videoActive = _videoActive;
            double milliseconds = deltaSeconds * 1000.0;
            if (double.IsFinite(milliseconds)
                && milliseconds > 25.0
                && _slowFrameSamples.Count < MaxSlowFrameSamples)
            {
                _slowFrameSamples.Add(new TransitionSlowFrameSample(
                    Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds,
                    milliseconds,
                    videoActive ? videoPositionSeconds : null));
            }
        }

        _sessionFrames.Record(deltaSeconds);
        if (videoActive)
        {
            _videoFrames.Record(deltaSeconds);
        }
    }

    public void RecordStreamAcquire(TimeSpan elapsed)
    {
        lock (_sync)
        {
            if (_completedSnapshot is null)
            {
                _streamAcquireMilliseconds = elapsed.TotalMilliseconds;
            }
        }
    }

    public void RecordPlayCall(TimeSpan elapsed)
    {
        lock (_sync)
        {
            if (_completedSnapshot is null)
            {
                _playCallMilliseconds = elapsed.TotalMilliseconds;
            }
        }
    }

    public void MarkVideoStarted()
    {
        lock (_sync)
        {
            if (_completedSnapshot is not null)
            {
                return;
            }

            _videoStartedAt = Stopwatch.GetTimestamp();
            _videoActive = true;
        }
    }

    public void RecordFirstPostPlayFrame()
    {
        lock (_sync)
        {
            if (_completedSnapshot is null && _videoStartedAt != 0 && _firstPostPlayFrameMilliseconds is null)
            {
                _firstPostPlayFrameMilliseconds = Stopwatch.GetElapsedTime(_videoStartedAt).TotalMilliseconds;
            }
        }
    }

    public void MarkVideoStopped()
    {
        lock (_sync)
        {
            if (!_videoActive)
            {
                return;
            }

            _videoActive = false;
            if (_videoStartedAt != 0)
            {
                _videoMilliseconds = Stopwatch.GetElapsedTime(_videoStartedAt).TotalMilliseconds;
            }
        }
    }

    public void RecordFinalizeBatch(int count, TimeSpan elapsed)
    {
        lock (_sync)
        {
            if (_completedSnapshot is not null)
            {
                return;
            }

            _finalizedResourceCount += count;
            if (count > 0)
            {
                _finalizeBatchCount++;
                _longestFinalizeBatchMilliseconds = Math.Max(
                    _longestFinalizeBatchMilliseconds,
                    elapsed.TotalMilliseconds);
            }
        }
    }

    public void RecordDeferredGc()
    {
        lock (_sync)
        {
            if (_completedSnapshot is null)
            {
                _deferredGcAtMilliseconds.Add(Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds);
            }
        }
    }

    public void RecordPhase(string name, TimeSpan elapsed)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        lock (_sync)
        {
            if (_completedSnapshot is not null || _phaseSamples.Count >= MaxPhaseSamples)
            {
                return;
            }

            double durationMilliseconds = Math.Max(0.0, elapsed.TotalMilliseconds);
            double completedAtMilliseconds = Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds;
            _phaseSamples.Add(new TransitionPhaseSample(
                name,
                Math.Max(0.0, completedAtMilliseconds - durationMilliseconds),
                durationMilliseconds));
        }
    }

    public TransitionPerformanceSnapshot Complete(
        TransitionCompletionStatus status,
        TransitionGcCounts endingGcCounts,
        TransitionGcFlushResult gcFlush,
        TransitionNoGcRegionCompletion noGcRegion = default)
    {
        lock (_sync)
        {
            if (_completedSnapshot is not null)
            {
                return _completedSnapshot;
            }

            MarkVideoStopped();
            var naturalGcDelta = new TransitionGcCounts(
                Math.Max(0, endingGcCounts.Generation0 - _startingGcCounts.Generation0),
                Math.Max(0, endingGcCounts.Generation1 - _startingGcCounts.Generation1),
                Math.Max(0, endingGcCounts.Generation2 - _startingGcCounts.Generation2));
            _completedSnapshot = new TransitionPerformanceSnapshot(
                SessionId,
                Kind,
                status,
                Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds,
                _videoMilliseconds,
                _streamAcquireMilliseconds,
                _playCallMilliseconds,
                _firstPostPlayFrameMilliseconds,
                _sessionFrames.Snapshot(),
                _videoFrames.Snapshot(),
                _slowFrameSamples.ToArray(),
                _phaseSamples.ToArray(),
                _finalizedResourceCount,
                _finalizeBatchCount,
                _longestFinalizeBatchMilliseconds,
                _deferredGcAtMilliseconds.ToArray(),
                naturalGcDelta,
                gcFlush,
                noGcRegion,
                Math.Max(0, GC.GetTotalAllocatedBytes(precise: false) - _startingAllocatedBytes));
            return _completedSnapshot;
        }
    }
}
