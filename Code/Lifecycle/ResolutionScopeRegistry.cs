using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace NinjaSlayer.Code.Lifecycle;

internal sealed class ResolutionScopeRegistry<TSubject, TScope>
    where TSubject : class
    where TScope : class
{
    private readonly Lock _lock = new();
    private readonly Dictionary<TSubject, List<ScopeEntry>> _entriesBySubject =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<TScope, ScopeEntry> _entriesByScope =
        new(ReferenceEqualityComparer.Instance);
    public void Begin(TSubject subject, TScope scope)
    {
        lock (_lock)
        {
            CompleteCore(scope);
            ScopeEntry entry = new(subject, scope);
            if (!_entriesBySubject.TryGetValue(subject, out List<ScopeEntry>? entries))
            {
                entries = [];
                _entriesBySubject.Add(subject, entries);
            }

            entries.Add(entry);
            _entriesByScope.Add(scope, entry);
        }
    }

    public bool TryGetLatestScope(TSubject subject, [NotNullWhen(true)] out TScope? scope)
    {
        lock (_lock)
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
        [NotNullWhen(true)] out TState? state)
        where TState : class
    {
        lock (_lock)
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

    public bool TryGetState<TState>(
        TScope scope,
        object owner,
        [NotNullWhen(true)] out TState? state)
        where TState : class
    {
        lock (_lock)
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

    public bool TryTakeState<TState>(
        TScope scope,
        object owner,
        [NotNullWhen(true)] out TState? state)
        where TState : class
    {
        lock (_lock)
        {
            if (_entriesByScope.TryGetValue(scope, out ScopeEntry? entry)
                && entry.States.Remove(new StateKey(owner, typeof(TState)), out object? existing)
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
        lock (_lock)
        {
            return CompleteCore(scope);
        }
    }

    public void CompleteSubject(TSubject subject)
    {
        lock (_lock)
        {
            if (!_entriesBySubject.Remove(subject, out List<ScopeEntry>? entries))
            {
                return;
            }

            foreach (ScopeEntry entry in entries)
            {
                _entriesByScope.Remove(entry.Scope);
            }

        }
    }

    public bool ForceClear()
    {
        lock (_lock)
        {
            bool hadEntries = _entriesByScope.Count > 0;
            _entriesBySubject.Clear();
            _entriesByScope.Clear();
            return hadEntries;
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

    private sealed class ScopeEntry(TSubject subject, TScope scope)
    {
        public TSubject Subject { get; } = subject;
        public TScope Scope { get; } = scope;
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
