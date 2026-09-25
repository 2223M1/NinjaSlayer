namespace NinjaSlayer.Code.ExternalAnimations;

// Store angles, never world positions. Exact animation curves avoid aliasing fast
// turns into slow or reversed turns when more than half a turn passes per frame.
internal sealed class SpinExposureHistory
{
    internal const double ExposureSeconds = 1001d / 24000d;
    internal const int SampleCount = 25;
    private readonly List<Sample> _history = [];
    private sealed record Sample(double Time, double Degrees, Func<double, double>? Before);

    internal void Clear() => _history.Clear();

    internal void Record(double time, double degrees, Func<double, double>? angleAtSecondsBefore = null,
        bool discontinuity = false)
    {
        if (!discontinuity && _history.Count > 0 && _history[^1].Time == time)
        {
            // A render-time transform correction must retain the Tween's trajectory.
            Sample old = _history[^1];
            _history[^1] = new(time, degrees, angleAtSecondsBefore ?? old.Before);
        }
        else _history.Add(new(time, degrees, angleAtSecondsBefore));
        while (_history.Count > 2 && _history[1].Time < time - ExposureSeconds)
            _history.RemoveAt(0);
    }

    internal bool SampleAngles(double time, Span<float> angles)
    {
        if (angles.Length != SampleCount) throw new ArgumentException("Exposure requires 25 samples.");
        bool moving = false;
        for (int i = 0; i < SampleCount; i++)
        {
            angles[i] = (float)AngleAt(time - ExposureSeconds * i / (SampleCount - 1));
            moving |= Math.Abs(angles[i] - angles[0]) > 0.0001f;
        }
        return moving;
    }

    private double AngleAt(double time)
    {
        if (_history.Count == 0) return 0;
        if (time >= _history[^1].Time) return _history[^1].Degrees;
        if (time <= _history[0].Time)
            return _history[0].Before?.Invoke(_history[0].Time - time) ?? _history[0].Degrees;
        for (int i = 1; i < _history.Count; i++)
        {
            Sample next = _history[i];
            if (time > next.Time) continue;
            Sample previous = _history[i - 1];
            return next.Before != null ? next.Before(next.Time - time)
                : previous.Degrees + (next.Degrees - previous.Degrees)
                    * (time - previous.Time) / (next.Time - previous.Time);
        }
        return _history[^1].Degrees;
    }
}
