namespace NinjaSlayer.Code.Telemetry;

public enum NinjaSlayerTelemetryCharacterKind
{
    Unknown,
    Official,
    Debug,
    Other
}

public enum NinjaSlayerTelemetryIdentityStatus
{
    NoActiveRun,
    AwaitingIdentity,
    Eligible,
    Ineligible,
    Ended
}

public readonly record struct NinjaSlayerTelemetryPlayerIdentity(
    ulong NetId,
    NinjaSlayerTelemetryCharacterKind CharacterKind);

public sealed class NinjaSlayerTelemetryIdentityTracker
{
    private readonly object _gate = new();

    private object? _activeRunToken;
    private object? _endedRunToken;
    private ulong? _localNetId;
    private bool _endedCaptureEligible;
    private bool _endedCaptureConsumed;
    private long _generation;
    private NinjaSlayerTelemetryIdentityStatus _status = NinjaSlayerTelemetryIdentityStatus.NoActiveRun;

    public long Generation
    {
        get
        {
            lock (_gate)
            {
                return _generation;
            }
        }
    }

    public NinjaSlayerTelemetryIdentityStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    public void BeginRun(object runToken)
    {
        ArgumentNullException.ThrowIfNull(runToken);

        lock (_gate)
        {
            if (ReferenceEquals(_activeRunToken, runToken))
            {
                return;
            }

            _generation++;
            _activeRunToken = runToken;
            _endedRunToken = null;
            _localNetId = null;
            _endedCaptureEligible = false;
            _endedCaptureConsumed = false;
            _status = NinjaSlayerTelemetryIdentityStatus.AwaitingIdentity;
        }
    }

    public NinjaSlayerTelemetryIdentityStatus Refresh(
        object runToken,
        ulong? localNetId,
        IReadOnlyList<NinjaSlayerTelemetryPlayerIdentity> players)
    {
        ArgumentNullException.ThrowIfNull(runToken);
        ArgumentNullException.ThrowIfNull(players);

        lock (_gate)
        {
            if (!ReferenceEquals(_activeRunToken, runToken))
            {
                return _status;
            }

            _status = EvaluateIdentity(localNetId, players);
            return _status;
        }
    }

    public bool TryCaptureCompletedRun(
        object endedRunToken,
        bool isAbandoned,
        ulong? localNetId,
        IReadOnlyList<NinjaSlayerTelemetryPlayerIdentity> players)
    {
        ArgumentNullException.ThrowIfNull(endedRunToken);
        ArgumentNullException.ThrowIfNull(players);

        lock (_gate)
        {
            if (ReferenceEquals(_endedRunToken, endedRunToken))
            {
                if (_endedCaptureConsumed)
                {
                    return false;
                }

                _endedCaptureConsumed = true;
                return _endedCaptureEligible;
            }

            if (_activeRunToken is null || _endedRunToken is not null)
            {
                return false;
            }

            NinjaSlayerTelemetryIdentityStatus identity = EvaluateIdentity(localNetId, players);
            bool eligible = !isAbandoned && identity == NinjaSlayerTelemetryIdentityStatus.Eligible;
            EndActiveRun(endedRunToken, eligible, captureConsumed: true);
            return eligible;
        }
    }

    public void ObserveRunEnded(
        object endedRunToken,
        bool isAbandoned,
        ulong? localNetId,
        IReadOnlyList<NinjaSlayerTelemetryPlayerIdentity> players)
    {
        ArgumentNullException.ThrowIfNull(endedRunToken);
        ArgumentNullException.ThrowIfNull(players);

        lock (_gate)
        {
            if (ReferenceEquals(_endedRunToken, endedRunToken)
                || _activeRunToken is null
                || _endedRunToken is not null)
            {
                return;
            }

            NinjaSlayerTelemetryIdentityStatus identity = EvaluateIdentity(localNetId, players);
            bool eligible = !isAbandoned && identity == NinjaSlayerTelemetryIdentityStatus.Eligible;
            EndActiveRun(endedRunToken, eligible, captureConsumed: false);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _activeRunToken = null;
            _endedRunToken = null;
            _localNetId = null;
            _endedCaptureEligible = false;
            _endedCaptureConsumed = false;
            _status = NinjaSlayerTelemetryIdentityStatus.NoActiveRun;
        }
    }

    private NinjaSlayerTelemetryIdentityStatus EvaluateIdentity(
        ulong? localNetId,
        IReadOnlyList<NinjaSlayerTelemetryPlayerIdentity> players)
    {
        if (localNetId.HasValue)
        {
            _localNetId = localNetId;
        }

        if (!_localNetId.HasValue)
        {
            return NinjaSlayerTelemetryIdentityStatus.AwaitingIdentity;
        }

        NinjaSlayerTelemetryPlayerIdentity localPlayer = default;
        int matchCount = 0;
        foreach (NinjaSlayerTelemetryPlayerIdentity player in players)
        {
            if (player.NetId != _localNetId.Value)
            {
                continue;
            }

            localPlayer = player;
            matchCount++;
        }

        if (matchCount != 1)
        {
            return NinjaSlayerTelemetryIdentityStatus.AwaitingIdentity;
        }

        return localPlayer.CharacterKind switch
        {
            NinjaSlayerTelemetryCharacterKind.Official => NinjaSlayerTelemetryIdentityStatus.Eligible,
            NinjaSlayerTelemetryCharacterKind.Debug or NinjaSlayerTelemetryCharacterKind.Other =>
                NinjaSlayerTelemetryIdentityStatus.Ineligible,
            _ => NinjaSlayerTelemetryIdentityStatus.AwaitingIdentity
        };
    }

    private void EndActiveRun(object endedRunToken, bool eligible, bool captureConsumed)
    {
        _activeRunToken = null;
        _endedRunToken = endedRunToken;
        _localNetId = null;
        _endedCaptureEligible = eligible;
        _endedCaptureConsumed = captureConsumed;
        _status = NinjaSlayerTelemetryIdentityStatus.Ended;
    }
}
