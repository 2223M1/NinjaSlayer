using System.Runtime.CompilerServices;

namespace NinjaSlayer.Code.Lifecycle;

public sealed class ResolutionScopeRegistry<TSubject, TScope>
    where TSubject : class
    where TScope : class
{
    private readonly object _sync = new();
    private readonly Dictionary<TSubject, List<ScopeEntry>> _entriesBySubject =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<TScope, ScopeEntry> _entriesByScope =
        new(ReferenceEqualityComparer.Instance);
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly Action<string>? _violationReporter;
    private readonly bool _throwOnThreadViolation;
    private long _nextSequence;
    private int _threadViolationReported;

    public ResolutionScopeRegistry(
        Action<string>? violationReporter = null,
        bool? throwOnThreadViolation = null)
    {
        _violationReporter = violationReporter;
        _throwOnThreadViolation = throwOnThreadViolation ?? DefaultStrictThreadAffinity;
    }

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _entriesByScope.Count;
            }
        }
    }

    public bool Begin(TSubject subject, TScope scope)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(scope);
        if (!CheckThreadAffinity(nameof(Begin)))
        {
            return false;
        }

        lock (_sync)
        {
            CompleteCore(scope);
            ScopeEntry entry = new(subject, scope, ++_nextSequence);
            if (!_entriesBySubject.TryGetValue(subject, out List<ScopeEntry>? entries))
            {
                entries = [];
                _entriesBySubject.Add(subject, entries);
            }

            entries.Add(entry);
            _entriesByScope.Add(scope, entry);
            return true;
        }
    }

    public bool TryGetLatestScope(TSubject subject, out TScope? scope)
    {
        if (!CheckThreadAffinity(nameof(TryGetLatestScope)))
        {
            scope = null;
            return false;
        }

        lock (_sync)
        {
            if (_entriesBySubject.TryGetValue(subject, out List<ScopeEntry>? entries) && entries.Count > 0)
            {
                scope = entries[^1].Scope;
                return true;
            }
        }

        scope = null;
        return false;
    }

    public bool TryGetOrCreateState<TState>(
        TScope scope,
        object owner,
        Func<TState> factory,
        out TState? state)
        where TState : class
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(factory);
        if (!CheckThreadAffinity(nameof(TryGetOrCreateState)))
        {
            state = null;
            return false;
        }

        lock (_sync)
        {
            if (!_entriesByScope.TryGetValue(scope, out ScopeEntry? entry))
            {
                state = null;
                return false;
            }

            var key = new StateKey(owner, typeof(TState));
            if (entry.States.TryGetValue(key, out object? existing))
            {
                state = (TState)existing;
                return true;
            }

            state = factory();
            entry.States.Add(key, state);
            return true;
        }
    }

    public bool TryGetState<TState>(TScope scope, object owner, out TState? state)
        where TState : class
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(owner);
        if (!CheckThreadAffinity(nameof(TryGetState)))
        {
            state = null;
            return false;
        }

        lock (_sync)
        {
            if (_entriesByScope.TryGetValue(scope, out ScopeEntry? entry)
                && entry.States.TryGetValue(new StateKey(owner, typeof(TState)), out object? existing)
                && existing is TState typed)
            {
                state = typed;
                return true;
            }
        }

        state = null;
        return false;
    }

    public bool Complete(TScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (!CheckThreadAffinity(nameof(Complete)))
        {
            return false;
        }

        lock (_sync)
        {
            return CompleteCore(scope);
        }
    }

    public int CompleteSubject(TSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (!CheckThreadAffinity(nameof(CompleteSubject)))
        {
            return 0;
        }

        lock (_sync)
        {
            if (!_entriesBySubject.Remove(subject, out List<ScopeEntry>? entries))
            {
                return 0;
            }

            foreach (ScopeEntry entry in entries)
            {
                _entriesByScope.Remove(entry.Scope);
            }

            return entries.Count;
        }
    }

    public int ForceClear()
    {
        lock (_sync)
        {
            int count = _entriesByScope.Count;
            _entriesBySubject.Clear();
            _entriesByScope.Clear();
            return count;
        }
    }

    private bool CompleteCore(TScope scope)
    {
        if (!_entriesByScope.Remove(scope, out ScopeEntry? entry))
        {
            return false;
        }

        if (_entriesBySubject.TryGetValue(entry.Subject, out List<ScopeEntry>? entries))
        {
            entries.Remove(entry);
            if (entries.Count == 0)
            {
                _entriesBySubject.Remove(entry.Subject);
            }
        }

        return true;
    }

    private bool CheckThreadAffinity(string operation)
    {
        int currentThreadId = Environment.CurrentManagedThreadId;
        if (currentThreadId == _ownerThreadId)
        {
            return true;
        }

        string message =
            $"Resolution scope operation {operation} ran on thread {currentThreadId}; owner thread is {_ownerThreadId}.";
        if (_throwOnThreadViolation)
        {
            throw new InvalidOperationException(message);
        }

        if (Interlocked.Exchange(ref _threadViolationReported, 1) == 0)
        {
            _violationReporter?.Invoke(message);
        }

        return false;
    }

#if DEBUG
    private const bool DefaultStrictThreadAffinity = true;
#else
    private const bool DefaultStrictThreadAffinity = false;
#endif

    private sealed class ScopeEntry(TSubject subject, TScope scope, long sequence)
    {
        public TSubject Subject { get; } = subject;
        public TScope Scope { get; } = scope;
        public long Sequence { get; } = sequence;
        public Dictionary<StateKey, object> States { get; } = [];
    }

    private readonly struct StateKey(object owner, Type stateType) : IEquatable<StateKey>
    {
        private object Owner { get; } = owner;
        private Type StateType { get; } = stateType;

        public bool Equals(StateKey other) =>
            ReferenceEquals(Owner, other.Owner) && StateType == other.StateType;

        public override bool Equals(object? obj) => obj is StateKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(Owner), StateType);
    }
}
