namespace NinjaSlayer.Code.Lifecycle;

internal sealed class AsyncScopeDepth
{
    private readonly AsyncLocal<Lease?> _current = new();

    public bool IsActive => FindActive(_current.Value) is not null;

    public IDisposable Enter()
    {
        var lease = new Lease(this, FindActive(_current.Value));
        _current.Value = lease;
        return lease;
    }

    private void Release(Lease lease)
    {
        if (ReferenceEquals(_current.Value, lease))
        {
            _current.Value = FindActive(lease.Parent);
        }
    }

    private static Lease? FindActive(Lease? lease)
    {
        while (lease?.IsDisposed == true)
        {
            lease = lease.Parent;
        }

        return lease;
    }

    private sealed class Lease(AsyncScopeDepth owner, Lease? parent) : IDisposable
    {
        private int _disposed;

        public Lease? Parent { get; } = parent;
        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Release(this);
            }
        }
    }
}
