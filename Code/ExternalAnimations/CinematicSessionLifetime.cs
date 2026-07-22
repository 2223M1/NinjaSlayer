namespace NinjaSlayer.Code.ExternalAnimations;

internal sealed class CinematicSessionLifetime : IDisposable
{
    private readonly CancellationTokenSource _source = new();
    private readonly CancellationToken _token;
    private readonly object _sync = new();
    private int _disposed;

    public CinematicSessionLifetime()
    {
        _token = _source.Token;
    }

    public CancellationToken Token => _token;
    public bool IsCancellationRequested => _token.IsCancellationRequested;
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public void Cancel()
    {
        lock (_sync)
        {
            if (!IsDisposed && !_source.IsCancellationRequested)
            {
                _source.Cancel();
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (!_source.IsCancellationRequested)
            {
                _source.Cancel();
            }
            _source.Dispose();
        }
    }
}
