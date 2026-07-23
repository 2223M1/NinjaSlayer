namespace NinjaSlayer.Code.Combat;

internal enum FinisherForecastOutcome
{
    Guaranteed,
    NotGuaranteed,
    IndeterminateBudget
}

internal enum FinisherForecastTargeting
{
    Single,
    All,
    Random,
    Fixed
}

internal enum FinisherForecastEffectTargeting
{
    All,
    Random
}

internal sealed record FinisherForecastPostEffect<TState>(
    FinisherForecastEffectTargeting Targeting,
    Func<TState[], IReadOnlyList<int>, bool> TryApply);

internal sealed record FinisherForecastSimulation<TState, TStateKey>(
    IReadOnlyList<TState> InitialStates,
    int HitCount,
    FinisherForecastTargeting Targeting,
    Func<TState, bool> IsAlive,
    Func<TState, TStateKey> StateKey,
    Func<TState[], IReadOnlyList<int>, int, bool> TryApplyHit,
    int? SingleTarget = null,
    IReadOnlyList<int>? FixedTargets = null,
    IReadOnlyList<FinisherForecastPostEffect<TState>>? PostEffects = null)
    where TStateKey : notnull;

internal enum FinisherForecastSearchStage
{
    Hits,
    Effects
}

internal static class FinisherForecastEngine
{
    public const int DefaultMaximumSearchStates = 25_000;
    public static readonly TimeSpan DefaultMaximumSearchTime = TimeSpan.FromMilliseconds(8);

    public static FinisherForecastOutcome Evaluate<TState, TStateKey>(
        FinisherForecastSimulation<TState, TStateKey> simulation,
        int maximumSearchStates = DefaultMaximumSearchStates,
        TimeSpan? maximumSearchTime = null)
        where TStateKey : notnull
    {
        if (simulation.HitCount <= 0 || simulation.InitialStates.Count == 0)
        {
            return FinisherForecastOutcome.NotGuaranteed;
        }

        var search = new SearchContext<TState, TStateKey>(
            simulation.StateKey,
            maximumSearchStates,
            maximumSearchTime ?? DefaultMaximumSearchTime);
        TState[] states = [.. simulation.InitialStates];

        return simulation.Targeting switch
        {
            FinisherForecastTargeting.Single when simulation.SingleTarget is int target
                && target >= 0
                && target < states.Length => SimulateFixed(simulation, states, [[target]], search),
            FinisherForecastTargeting.All => SimulateFixed(
                simulation,
                states,
                [AliveTargets(states, simulation.IsAlive)],
                search,
                resolveTargetsEachHit: true),
            FinisherForecastTargeting.Random => SimulateRandom(
                simulation,
                states,
                hitIndex: 0,
                simulation.HitCount,
                search),
            FinisherForecastTargeting.Fixed when simulation.FixedTargets is { Count: > 0 } fixedTargets
                && fixedTargets.All(target => target >= 0 && target < states.Length) => SimulateFixed(
                    simulation,
                    states,
                    [fixedTargets],
                    search,
                    resolveTargetsEachHit: true),
            _ => FinisherForecastOutcome.NotGuaranteed
        };
    }

    private static FinisherForecastOutcome SimulateFixed<TState, TStateKey>(
        FinisherForecastSimulation<TState, TStateKey> simulation,
        TState[] states,
        IReadOnlyList<IReadOnlyList<int>> targetSets,
        SearchContext<TState, TStateKey> search,
        bool resolveTargetsEachHit = false)
        where TStateKey : notnull
    {
        for (int hit = 0; hit < simulation.HitCount; hit++)
        {
            IReadOnlyList<int> targets = resolveTargetsEachHit
                ? targetSets[0].Where(target => simulation.IsAlive(states[target])).ToArray()
                : targetSets[Math.Min(hit, targetSets.Count - 1)];
            if (targets.Count == 0)
            {
                break;
            }

            if (!simulation.TryApplyHit(states, targets, hit))
            {
                return FinisherForecastOutcome.NotGuaranteed;
            }
        }

        return ApplyPostEffects(simulation, states, effectIndex: 0, search);
    }

    private static FinisherForecastOutcome SimulateRandom<TState, TStateKey>(
        FinisherForecastSimulation<TState, TStateKey> simulation,
        TState[] states,
        int hitIndex,
        int hitsRemaining,
        SearchContext<TState, TStateKey> search)
        where TStateKey : notnull
    {
        if (hitsRemaining == 0)
        {
            return ApplyPostEffects(simulation, states, effectIndex: 0, search);
        }

        int[] alive = AliveTargets(states, simulation.IsAlive);
        if (alive.Length == 0)
        {
            return ApplyPostEffects(simulation, states, effectIndex: 0, search);
        }

        FinisherForecastSearchKey<TStateKey> key = search.CreateKey(
            FinisherForecastSearchStage.Hits,
            hitIndex,
            hitsRemaining,
            states);
        MemoSearchLookup lookup = search.Lookup(key, out FinisherForecastOutcome cached);
        if (lookup == MemoSearchLookup.Cached)
        {
            return cached;
        }

        if (lookup is MemoSearchLookup.StateBudgetExceeded or MemoSearchLookup.WatchdogExpired)
        {
            return FinisherForecastOutcome.IndeterminateBudget;
        }

        bool indeterminate = false;
        foreach (int target in alive)
        {
            TState[] branch = (TState[])states.Clone();
            if (!simulation.TryApplyHit(branch, [target], hitIndex))
            {
                search.Store(key, FinisherForecastOutcome.NotGuaranteed);
                return FinisherForecastOutcome.NotGuaranteed;
            }

            FinisherForecastOutcome branchResult = SimulateRandom(
                simulation,
                branch,
                hitIndex + 1,
                hitsRemaining - 1,
                search);
            if (branchResult == FinisherForecastOutcome.NotGuaranteed)
            {
                search.Store(key, branchResult);
                return branchResult;
            }

            indeterminate |= branchResult == FinisherForecastOutcome.IndeterminateBudget;
        }

        FinisherForecastOutcome result = indeterminate
            ? FinisherForecastOutcome.IndeterminateBudget
            : FinisherForecastOutcome.Guaranteed;
        search.Store(key, result);
        return result;
    }

    private static FinisherForecastOutcome ApplyPostEffects<TState, TStateKey>(
        FinisherForecastSimulation<TState, TStateKey> simulation,
        TState[] states,
        int effectIndex,
        SearchContext<TState, TStateKey> search)
        where TStateKey : notnull
    {
        IReadOnlyList<FinisherForecastPostEffect<TState>> effects = simulation.PostEffects ?? [];
        if (effectIndex >= effects.Count)
        {
            return states.All(state => !simulation.IsAlive(state))
                ? FinisherForecastOutcome.Guaranteed
                : FinisherForecastOutcome.NotGuaranteed;
        }

        int[] alive = AliveTargets(states, simulation.IsAlive);
        if (alive.Length == 0)
        {
            return FinisherForecastOutcome.Guaranteed;
        }

        FinisherForecastPostEffect<TState> effect = effects[effectIndex];
        if (effect.Targeting == FinisherForecastEffectTargeting.All)
        {
            if (!effect.TryApply(states, alive))
            {
                return FinisherForecastOutcome.NotGuaranteed;
            }

            return ApplyPostEffects(simulation, states, effectIndex + 1, search);
        }

        FinisherForecastSearchKey<TStateKey> key = search.CreateKey(
            FinisherForecastSearchStage.Effects,
            effectIndex,
            effects.Count,
            states);
        MemoSearchLookup lookup = search.Lookup(key, out FinisherForecastOutcome cached);
        if (lookup == MemoSearchLookup.Cached)
        {
            return cached;
        }

        if (lookup is MemoSearchLookup.StateBudgetExceeded or MemoSearchLookup.WatchdogExpired)
        {
            return FinisherForecastOutcome.IndeterminateBudget;
        }

        bool indeterminate = false;
        foreach (int target in alive)
        {
            TState[] branch = (TState[])states.Clone();
            if (!effect.TryApply(branch, [target]))
            {
                search.Store(key, FinisherForecastOutcome.NotGuaranteed);
                return FinisherForecastOutcome.NotGuaranteed;
            }

            FinisherForecastOutcome branchResult = ApplyPostEffects(simulation, branch, effectIndex + 1, search);
            if (branchResult == FinisherForecastOutcome.NotGuaranteed)
            {
                search.Store(key, branchResult);
                return branchResult;
            }

            indeterminate |= branchResult == FinisherForecastOutcome.IndeterminateBudget;
        }

        FinisherForecastOutcome result = indeterminate
            ? FinisherForecastOutcome.IndeterminateBudget
            : FinisherForecastOutcome.Guaranteed;
        search.Store(key, result);
        return result;
    }

    private static int[] AliveTargets<TState>(TState[] states, Func<TState, bool> isAlive) =>
        Enumerable.Range(0, states.Length).Where(index => isAlive(states[index])).ToArray();

    private sealed class SearchContext<TState, TStateKey>(
        Func<TState, TStateKey> stateKey,
        int maximumStates,
        TimeSpan maximumTime)
        where TStateKey : notnull
    {
        private readonly BoundedMemoSearch<FinisherForecastSearchKey<TStateKey>, FinisherForecastOutcome> _search =
            new(maximumStates, maximumTime);

        public MemoSearchLookup Lookup(
            FinisherForecastSearchKey<TStateKey> key,
            out FinisherForecastOutcome result) =>
            _search.Lookup(key, out result);

        public void Store(FinisherForecastSearchKey<TStateKey> key, FinisherForecastOutcome result) =>
            _search.Store(key, result);

        public FinisherForecastSearchKey<TStateKey> CreateKey(
            FinisherForecastSearchStage stage,
            int index,
            int remaining,
            IReadOnlyList<TState> states) =>
            new(stage, index, remaining, states.Select(stateKey));
    }
}

internal sealed class FinisherForecastSearchKey<TStateKey> : IEquatable<FinisherForecastSearchKey<TStateKey>>
    where TStateKey : notnull
{
    private readonly TStateKey[] _states;
    private readonly int _hashCode;

    public FinisherForecastSearchKey(
        FinisherForecastSearchStage stage,
        int index,
        int remaining,
        IEnumerable<TStateKey> states)
    {
        Stage = stage;
        Index = index;
        Remaining = remaining;
        _states = states.ToArray();

        var hash = new HashCode();
        hash.Add(Stage);
        hash.Add(Index);
        hash.Add(Remaining);
        foreach (TStateKey state in _states)
        {
            hash.Add(state);
        }

        _hashCode = hash.ToHashCode();
    }

    public FinisherForecastSearchStage Stage { get; }
    public int Index { get; }
    public int Remaining { get; }

    public bool Equals(FinisherForecastSearchKey<TStateKey>? other) =>
        other is not null
        && Stage == other.Stage
        && Index == other.Index
        && Remaining == other.Remaining
        && _states.AsSpan().SequenceEqual(other._states);

    public override bool Equals(object? obj) => Equals(obj as FinisherForecastSearchKey<TStateKey>);

    public override int GetHashCode() => _hashCode;
}
