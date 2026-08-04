namespace NinjaSlayer.Code.Transition;

internal sealed class TransitionGcDeferralState
{
    private readonly Lock _lock = new();
    private long _sessionId;
    private bool _collectionRequested;

    public bool IsActive
    {
        get
        {
            lock (_lock)
            {
                return _sessionId != 0;
            }
        }
    }

    public bool Begin(long sessionId)
    {
        lock (_lock)
        {
            bool inheritedRequest = _sessionId != 0 && _collectionRequested;
            _sessionId = sessionId;
            _collectionRequested = inheritedRequest;
            return inheritedRequest;
        }
    }

    public bool TryDefer()
    {
        lock (_lock)
        {
            if (_sessionId == 0)
            {
                return false;
            }

            _collectionRequested = true;
            return true;
        }
    }

    public Exception? Complete(long sessionId, Action requestCollection)
    {
        bool collectionRequested;
        lock (_lock)
        {
            if (_sessionId != sessionId)
            {
                return null;
            }

            collectionRequested = _collectionRequested;
            _sessionId = 0;
            _collectionRequested = false;
        }

        if (!collectionRequested)
        {
            return null;
        }

        try
        {
            requestCollection();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
