namespace NinjaSlayer.Code.Combat;

internal sealed class FrameScopedCache<TKey, TValue>
    where TKey : notnull
{
    private readonly Dictionary<TKey, TValue> _entries = [];
    private ulong? _frame;

    public int Count => _entries.Count;

    public bool TryGet(ulong frame, TKey key, out TValue value)
    {
        EnsureFrame(frame);
        return _entries.TryGetValue(key, out value!);
    }

    public void Store(ulong frame, TKey key, TValue value)
    {
        EnsureFrame(frame);
        _entries[key] = value;
    }

    private void EnsureFrame(ulong frame)
    {
        if (_frame == frame)
        {
            return;
        }

        _frame = frame;
        _entries.Clear();
    }
}
