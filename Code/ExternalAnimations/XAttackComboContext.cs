namespace NinjaSlayer.Code.ExternalAnimations;

public static class XAttackComboContext
{
    private static readonly AsyncLocal<State?> Current = new();

    public static bool Active => FindActive(Current.Value) is not null;

    public static int CurrentHitIndex
    {
        get => FindActive(Current.Value)?.CurrentHitIndex ?? 0;
        set
        {
            if (FindActive(Current.Value) is { } current)
            {
                Current.Value = current with { CurrentHitIndex = value };
            }
        }
    }

    public static int TotalHits => FindActive(Current.Value)?.TotalHits ?? 0;

    public static IDisposable Enter(int totalHits)
    {
        State? parent = FindActive(Current.Value);
        var lease = new Lease(parent);
        Current.Value = new State(lease, 0, totalHits);
        return lease;
    }

    private static State? FindActive(State? state)
    {
        while (state?.Lease.IsDisposed == true)
        {
            state = state.Value.Lease.Parent;
        }

        return state;
    }

    private readonly record struct State(Lease Lease, int CurrentHitIndex, int TotalHits);

    private sealed class Lease(State? parent) : IDisposable
    {
        private int _disposed;

        public State? Parent { get; } = parent;
        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (Current.Value is { } current && ReferenceEquals(current.Lease, this))
            {
                Current.Value = FindActive(Parent);
            }
        }
    }
}
