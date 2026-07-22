namespace NinjaSlayer.Code.Lifecycle;

public sealed class ResolutionScopeRegistry<TSubject, TScope>
    where TSubject : class
    where TScope : class
{
    private readonly Dictionary<TSubject, List<ScopeEntry>> _entriesBySubject =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<TScope, ScopeEntry> _entriesByScope =
        new(ReferenceEqualityComparer.Instance);
    private long _nextSequence;

    public int Count => _entriesByScope.Count;

    public void Begin(TSubject subject, TScope scope)
    {
        Complete(scope);
        ScopeEntry entry = new(subject, scope, ++_nextSequence);
        if (!_entriesBySubject.TryGetValue(subject, out List<ScopeEntry>? entries))
        {
            entries = [];
            _entriesBySubject.Add(subject, entries);
        }

        entries.Add(entry);
        _entriesByScope.Add(scope, entry);
    }

    public bool TryGetLatestScope(TSubject subject, out TScope? scope)
    {
        if (_entriesBySubject.TryGetValue(subject, out List<ScopeEntry>? entries) && entries.Count > 0)
        {
            scope = entries[^1].Scope;
            return true;
        }

        scope = null;
        return false;
    }

    public TState GetOrCreateState<TState>(TScope scope, object owner, Func<TState> factory)
        where TState : class
    {
        if (!_entriesByScope.TryGetValue(scope, out ScopeEntry? entry))
        {
            throw new InvalidOperationException("The resolution scope is not active.");
        }

        if (entry.States.TryGetValue(owner, out object? existing))
        {
            return (TState)existing;
        }

        TState state = factory();
        entry.States.Add(owner, state);
        return state;
    }

    public bool TryGetState<TState>(TScope scope, object owner, out TState? state)
        where TState : class
    {
        if (_entriesByScope.TryGetValue(scope, out ScopeEntry? entry)
            && entry.States.TryGetValue(owner, out object? existing)
            && existing is TState typed)
        {
            state = typed;
            return true;
        }

        state = null;
        return false;
    }

    public void Complete(TScope scope)
    {
        if (!_entriesByScope.Remove(scope, out ScopeEntry? entry))
        {
            return;
        }

        if (_entriesBySubject.TryGetValue(entry.Subject, out List<ScopeEntry>? entries))
        {
            entries.Remove(entry);
            if (entries.Count == 0)
            {
                _entriesBySubject.Remove(entry.Subject);
            }
        }
    }

    public void CompleteSubject(TSubject subject)
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

    private sealed class ScopeEntry(TSubject subject, TScope scope, long sequence)
    {
        public TSubject Subject { get; } = subject;
        public TScope Scope { get; } = scope;
        public long Sequence { get; } = sequence;
        public Dictionary<object, object> States { get; } = new(ReferenceEqualityComparer.Instance);
    }
}
