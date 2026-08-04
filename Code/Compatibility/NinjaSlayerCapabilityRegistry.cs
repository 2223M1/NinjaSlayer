namespace NinjaSlayer.Code.Compatibility;

internal sealed class NinjaSlayerCapabilityRegistry
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, CapabilityStatus> _statuses = new(StringComparer.Ordinal);

    public static NinjaSlayerCapabilityRegistry Current { get; } = new();

    public void Publish(string capabilityId, CapabilityStatus status)
    {
        lock (_lock)
        {
            _statuses[capabilityId] = status;
        }
    }

    public bool IsOperational(string capabilityId)
    {
        lock (_lock)
        {
            return _statuses.TryGetValue(capabilityId, out CapabilityStatus? status)
                && status.IsOperational;
        }
    }

    public Dictionary<string, CapabilityStatus> Snapshot()
    {
        lock (_lock)
        {
            return new Dictionary<string, CapabilityStatus>(_statuses, StringComparer.Ordinal);
        }
    }
}
