namespace NinjaSlayer.Code.Transition;

internal enum TransitionVideoPrewarmPhase
{
    Idle,
    Running,
    Warmed,
    PlaybackStarted
}

internal sealed class TransitionVideoPrewarmState(int maxAttempts)
{
    private readonly object _syncRoot = new();
    private long _generation;
    private int _attempts;
    private TransitionVideoPrewarmPhase _phase;

    public TransitionVideoPrewarmPhase Phase
    {
        get
        {
            lock (_syncRoot)
            {
                return _phase;
            }
        }
    }

    public int Attempts
    {
        get
        {
            lock (_syncRoot)
            {
                return _attempts;
            }
        }
    }

    public bool TryBegin(out long generation)
    {
        lock (_syncRoot)
        {
            if (_phase != TransitionVideoPrewarmPhase.Idle || _attempts >= maxAttempts)
            {
                generation = 0;
                return false;
            }

            _attempts++;
            generation = ++_generation;
            _phase = TransitionVideoPrewarmPhase.Running;
            return true;
        }
    }

    public bool TryMarkWarmed(long generation)
    {
        lock (_syncRoot)
        {
            if (!IsCurrentRun(generation))
            {
                return false;
            }

            _phase = TransitionVideoPrewarmPhase.Warmed;
            return true;
        }
    }

    public bool TryReturnToIdle(long generation)
    {
        lock (_syncRoot)
        {
            if (!IsCurrentRun(generation))
            {
                return false;
            }

            _phase = TransitionVideoPrewarmPhase.Idle;
            return true;
        }
    }

    public long? BeginPlayback()
    {
        lock (_syncRoot)
        {
            if (_phase == TransitionVideoPrewarmPhase.PlaybackStarted)
            {
                return null;
            }

            long? interruptedGeneration = _phase == TransitionVideoPrewarmPhase.Running
                ? _generation
                : null;
            _generation++;
            _phase = TransitionVideoPrewarmPhase.PlaybackStarted;
            return interruptedGeneration;
        }
    }

    private bool IsCurrentRun(long generation) =>
        _phase == TransitionVideoPrewarmPhase.Running && generation == _generation;
}
