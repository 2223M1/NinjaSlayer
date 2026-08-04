namespace NinjaSlayer.Code.Combat;

internal sealed class BlackFlameDamageReceiverSet<TReceiver>
    where TReceiver : class
{
    private readonly object _syncRoot = new();
    private readonly HashSet<TReceiver> _seen = new(ReferenceEqualityComparer.Instance);
    private readonly List<TReceiver> _ordered = [];

    public void Record(TReceiver receiver, decimal totalDamage)
    {
        if (totalDamage <= 0)
        {
            return;
        }

        lock (_syncRoot)
        {
            if (_seen.Add(receiver))
            {
                _ordered.Add(receiver);
            }
        }
    }

    public IReadOnlyList<TReceiver> SnapshotWhere(Func<TReceiver, bool> predicate)
    {
        lock (_syncRoot)
        {
            return _ordered.Where(predicate).ToArray();
        }
    }
}
