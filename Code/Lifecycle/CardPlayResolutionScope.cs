using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.Lifecycle;

internal static class CardPlayResolutionScope
{
    private static readonly Dictionary<CardModel, List<CardResolution>> ResolutionsByCard =
        new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<CardPlay, ActivePlay> ActivePlaysByCardPlay =
        new(ReferenceEqualityComparer.Instance);

    public static CardResolution BeginCard(CardModel card)
    {
        CardResolution resolution = new(card);
        if (!ResolutionsByCard.TryGetValue(card, out List<CardResolution>? resolutions))
        {
            resolutions = [];
            ResolutionsByCard.Add(card, resolutions);
        }

        resolutions.Add(resolution);
        return resolution;
    }

    public static void BeginPlay(CardPlay cardPlay)
    {
        if (TryGetLatestResolution(cardPlay.Card, out CardResolution? resolution))
        {
            CompletePlay(cardPlay);
            var activePlay = new ActivePlay(resolution, cardPlay);
            resolution.ActivePlays.Add(activePlay);
            ActivePlaysByCardPlay.Add(cardPlay, activePlay);
        }
    }

    public static TState? GetOrCreatePlayState<TState>(CardPlay cardPlay, object owner, Func<TState> factory)
        where TState : class
    {
        if (!TryGetLatestResolution(cardPlay.Card, out CardResolution? resolution)
            || resolution.ActivePlays.Count == 0
            || !ReferenceEquals(resolution.ActivePlays[^1].CardPlay, cardPlay)
            || !ActivePlaysByCardPlay.TryGetValue(cardPlay, out ActivePlay? activePlay))
        {
            return null;
        }

        return GetOrCreateState(activePlay.States, owner, factory);
    }

    public static bool TryResolveCurrentPlay(
        CardModel card,
        [NotNullWhen(true)] out CardPlay? cardPlay)
    {
        if (TryGetLatestResolution(card, out CardResolution? resolution)
            && resolution.ActivePlays.Count > 0)
        {
            cardPlay = resolution.ActivePlays[^1].CardPlay;
            return true;
        }

        cardPlay = null;
        return false;
    }

    public static bool TryTakePlayState<TState>(
        CardPlay cardPlay,
        object owner,
        [NotNullWhen(true)] out TState? state)
        where TState : class
    {
        if (ActivePlaysByCardPlay.TryGetValue(cardPlay, out ActivePlay? activePlay)
            && activePlay.States.Remove(new StateKey(owner, typeof(TState)), out object? existing)
            && existing is TState typed)
        {
            state = typed;
            return true;
        }

        state = null;
        return false;
    }

    public static bool TryGetLatestPlayState<TState>(
        CardModel card,
        object owner,
        [NotNullWhen(true)] out TState? state)
        where TState : class
    {
        if (TryGetLatestResolution(card, out CardResolution? resolution)
            && resolution.ActivePlays.Count > 0)
        {
            return TryGetState(resolution.ActivePlays[^1].States, owner, out state);
        }

        state = null;
        return false;
    }

    public static TState? GetOrCreateCardState<TState>(CardModel card, object owner, Func<TState> factory)
        where TState : class
    {
        return TryGetLatestResolution(card, out CardResolution? resolution)
            ? GetOrCreateState(resolution.States, owner, factory)
            : null;
    }

    public static bool TryGetCardState<TState>(
        CardModel card,
        object owner,
        [NotNullWhen(true)] out TState? state)
        where TState : class
    {
        if (TryGetLatestResolution(card, out CardResolution? resolution))
        {
            return TryGetState(resolution.States, owner, out state);
        }

        state = null;
        return false;
    }

    public static async Task CompletePlayAfter(Task task, CardPlay cardPlay)
    {
        try
        {
            await task;
        }
        finally
        {
            CompletePlay(cardPlay);
        }
    }

    public static async Task CompleteCardAfter(Task task, CardResolution resolution)
    {
        try
        {
            await task;
        }
        finally
        {
            CompleteCard(resolution);
        }
    }

    public static void ResetAtLifecycleBoundary(string boundary)
    {
        bool hadEntries = ActivePlaysByCardPlay.Count > 0 || ResolutionsByCard.Count > 0;
        ActivePlaysByCardPlay.Clear();
        ResolutionsByCard.Clear();
        if (hadEntries)
        {
            Entry.Logger.Warn($"Force-cleared resolution scopes at {boundary}.");
        }
    }

    internal sealed class CardResolution(CardModel card)
    {
        public CardModel Card { get; } = card;
        internal List<ActivePlay> ActivePlays { get; } = [];
        internal Dictionary<StateKey, object> States { get; } = [];
    }

    internal sealed class ActivePlay(CardResolution resolution, CardPlay cardPlay)
    {
        public CardResolution Resolution { get; } = resolution;
        public CardPlay CardPlay { get; } = cardPlay;
        public Dictionary<StateKey, object> States { get; } = [];
    }

    private static bool TryGetLatestResolution(
        CardModel card,
        [NotNullWhen(true)] out CardResolution? resolution)
    {
        if (ResolutionsByCard.TryGetValue(card, out List<CardResolution>? resolutions)
            && resolutions.Count > 0)
        {
            resolution = resolutions[^1];
            return true;
        }

        resolution = null;
        return false;
    }

    private static TState GetOrCreateState<TState>(
        Dictionary<StateKey, object> states,
        object owner,
        Func<TState> factory)
        where TState : class
    {
        var key = new StateKey(owner, typeof(TState));
        if (states.TryGetValue(key, out object? existing))
        {
            return (TState)existing;
        }

        TState state = factory();
        states.Add(key, state);
        return state;
    }

    private static bool TryGetState<TState>(
        Dictionary<StateKey, object> states,
        object owner,
        [NotNullWhen(true)] out TState? state)
        where TState : class
    {
        if (states.TryGetValue(new StateKey(owner, typeof(TState)), out object? existing)
            && existing is TState typed)
        {
            state = typed;
            return true;
        }

        state = null;
        return false;
    }

    private static void CompletePlay(CardPlay cardPlay)
    {
        if (!ActivePlaysByCardPlay.Remove(cardPlay, out ActivePlay? activePlay))
        {
            return;
        }

        activePlay.Resolution.ActivePlays.Remove(activePlay);
    }

    private static void CompleteCard(CardResolution resolution)
    {
        foreach (ActivePlay activePlay in resolution.ActivePlays)
        {
            ActivePlaysByCardPlay.Remove(activePlay.CardPlay);
        }

        resolution.ActivePlays.Clear();
        if (!ResolutionsByCard.TryGetValue(resolution.Card, out List<CardResolution>? resolutions))
        {
            return;
        }

        resolutions.Remove(resolution);
        if (resolutions.Count == 0)
        {
            ResolutionsByCard.Remove(resolution.Card);
        }
    }

    internal readonly struct StateKey(object owner, Type stateType) : IEquatable<StateKey>
    {
        private object Owner { get; } = owner;
        private Type StateType { get; } = stateType;

        public bool Equals(StateKey other) =>
            ReferenceEquals(Owner, other.Owner) && StateType == other.StateType;

        public override bool Equals(object? obj) => obj is StateKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(Owner), StateType);
    }
}
