namespace NinjaSlayer.Code.Transition;

internal readonly record struct TransitionFrameDropDecision(
    bool ShouldSeek,
    double TargetPositionSeconds,
    int SkippedFrames,
    double LagSeconds)
{
    public static TransitionFrameDropDecision None => default;
}

internal sealed class TransitionFrameDropClock
{
    public const double FrameRate = 24.0;
    public const double FrameDurationSeconds = 1.0 / FrameRate;
    public const double SeekCooldownSeconds = FrameDurationSeconds * 2.0;

    private const double ComparisonToleranceSeconds = 0.000001;
    private readonly double _durationSeconds;
    private double _lastSeekWallSeconds = double.NegativeInfinity;

    public TransitionFrameDropClock(double durationSeconds)
    {
        _durationSeconds = double.IsFinite(durationSeconds)
            ? Math.Max(0.0, durationSeconds)
            : 0.0;
    }

    public bool HasEnded(double wallElapsedSeconds) =>
        double.IsFinite(wallElapsedSeconds)
        && wallElapsedSeconds >= _durationSeconds;

    public TransitionFrameDropDecision Evaluate(
        double wallElapsedSeconds,
        double streamPositionSeconds)
    {
        if (_durationSeconds <= 0.0
            || !double.IsFinite(wallElapsedSeconds)
            || !double.IsFinite(streamPositionSeconds)
            || wallElapsedSeconds < 0.0
            || streamPositionSeconds < 0.0)
        {
            return TransitionFrameDropDecision.None;
        }

        double lastFramePosition = Math.Max(0.0, _durationSeconds - FrameDurationSeconds);
        double expectedPosition = QuantizeToFrame(Math.Min(wallElapsedSeconds, lastFramePosition));
        double lagSeconds = expectedPosition - streamPositionSeconds;
        if (lagSeconds + ComparisonToleranceSeconds < FrameDurationSeconds
            || wallElapsedSeconds - _lastSeekWallSeconds + ComparisonToleranceSeconds < SeekCooldownSeconds)
        {
            return TransitionFrameDropDecision.None;
        }

        int skippedFrames = Math.Max(
            1,
            (int)Math.Floor((lagSeconds + ComparisonToleranceSeconds) / FrameDurationSeconds));
        _lastSeekWallSeconds = wallElapsedSeconds;
        return new TransitionFrameDropDecision(
            ShouldSeek: true,
            expectedPosition,
            skippedFrames,
            lagSeconds);
    }

    private static double QuantizeToFrame(double positionSeconds) =>
        Math.Floor((positionSeconds + ComparisonToleranceSeconds) * FrameRate) / FrameRate;
}
