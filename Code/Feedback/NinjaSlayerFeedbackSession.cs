namespace NinjaSlayer.Code.Feedback;

public readonly record struct NinjaSlayerFeedbackSessionToken(long Generation, ulong ScreenInstanceId);

public static class NinjaSlayerFeedbackSession
{
    private static readonly object SyncRoot = new();
    private static long _generation;
    private static ulong _screenInstanceId;
    private static bool _active;
    private static bool _confirmed;

    public static void Begin()
    {
        lock (SyncRoot)
        {
            _generation++;
            _screenInstanceId = 0;
            _active = true;
            _confirmed = false;
        }
    }

    public static bool TryBindScreen(ulong screenInstanceId, out NinjaSlayerFeedbackSessionToken token)
    {
        lock (SyncRoot)
        {
            if (!_active
                || screenInstanceId == 0
                || (_screenInstanceId != 0 && _screenInstanceId != screenInstanceId))
            {
                token = default;
                return false;
            }

            _screenInstanceId = screenInstanceId;
            token = CurrentToken();
            return true;
        }
    }

    public static bool TryGetCurrentToken(
        ulong screenInstanceId,
        out NinjaSlayerFeedbackSessionToken token)
    {
        lock (SyncRoot)
        {
            if (!MatchesScreen(screenInstanceId))
            {
                token = default;
                return false;
            }

            token = CurrentToken();
            return true;
        }
    }

    public static bool IsCurrent(NinjaSlayerFeedbackSessionToken token)
    {
        lock (SyncRoot)
        {
            return MatchesToken(token);
        }
    }

    public static bool IsConfirmed(NinjaSlayerFeedbackSessionToken token)
    {
        lock (SyncRoot)
        {
            return MatchesToken(token) && _confirmed;
        }
    }

    public static bool TryGetConfirmedToken(out NinjaSlayerFeedbackSessionToken token)
    {
        lock (SyncRoot)
        {
            if (!_active || !_confirmed || _screenInstanceId == 0)
            {
                token = default;
                return false;
            }

            token = CurrentToken();
            return true;
        }
    }

    public static bool TryConfirm(NinjaSlayerFeedbackSessionToken token)
    {
        lock (SyncRoot)
        {
            if (!MatchesToken(token) || _confirmed)
            {
                return false;
            }

            _confirmed = true;
            return true;
        }
    }

    public static bool ResetForScreen(ulong screenInstanceId)
    {
        lock (SyncRoot)
        {
            if (!_active || (_screenInstanceId != 0 && _screenInstanceId != screenInstanceId))
            {
                return false;
            }

            ResetLocked();
            return true;
        }
    }

    public static void Reset()
    {
        lock (SyncRoot)
        {
            ResetLocked();
        }
    }

    private static bool MatchesScreen(ulong screenInstanceId) =>
        _active && _screenInstanceId != 0 && _screenInstanceId == screenInstanceId;

    private static bool MatchesToken(NinjaSlayerFeedbackSessionToken token) =>
        MatchesScreen(token.ScreenInstanceId) && _generation == token.Generation;

    private static NinjaSlayerFeedbackSessionToken CurrentToken() =>
        new(_generation, _screenInstanceId);

    private static void ResetLocked()
    {
        _generation++;
        _screenInstanceId = 0;
        _active = false;
        _confirmed = false;
    }
}
