using System.Collections.ObjectModel;

namespace NinjaSlayer.Code.Compatibility;

internal sealed class NinjaSlayerCapabilityRegistry
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, CapabilityStatus> _statuses = new(StringComparer.Ordinal);

    public static NinjaSlayerCapabilityRegistry Current { get; } = new();

    public void Publish(string capabilityId, CapabilityStatus status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capabilityId);
        ArgumentNullException.ThrowIfNull(status);

        lock (_lock)
        {
            _statuses[capabilityId] = status;
        }
    }

    public CapabilityStatus Get(string capabilityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capabilityId);
        lock (_lock)
        {
            return _statuses.GetValueOrDefault(capabilityId, CapabilityStatus.NotEvaluated);
        }
    }

    public bool IsOperational(string capabilityId) => Get(capabilityId).IsOperational;

    public IReadOnlyDictionary<string, CapabilityStatus> Snapshot()
    {
        lock (_lock)
        {
            return new ReadOnlyDictionary<string, CapabilityStatus>(
                new Dictionary<string, CapabilityStatus>(_statuses, StringComparer.Ordinal));
        }
    }
}
