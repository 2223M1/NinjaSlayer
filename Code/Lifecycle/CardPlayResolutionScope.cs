using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace NinjaSlayer.Code.Lifecycle;

internal static class CardPlayResolutionScope
{
    private static readonly ResolutionScopeRegistry<CardModel, CardResolution> CardScopes = new();
    private static readonly ResolutionScopeRegistry<CardResolution, CardPlay> PlayScopes = new();

    public static CardResolution BeginCard(CardModel card)
    {
        CardResolution resolution = new(card);
        CardScopes.Begin(card, resolution);
        return resolution;
    }

    public static void BeginPlay(CardPlay cardPlay)
    {
        if (CardScopes.TryGetLatestScope(cardPlay.Card, out CardResolution? resolution)
            && resolution is not null)
        {
            PlayScopes.Begin(resolution, cardPlay);
        }
    }

    public static TState? GetOrCreatePlayState<TState>(CardPlay cardPlay, object owner, Func<TState> factory)
        where TState : class
    {
        return CardScopes.TryGetLatestScope(cardPlay.Card, out CardResolution? resolution)
            && resolution is not null
            && PlayScopes.TryGetLatestScope(resolution, out CardPlay? activePlay)
            && ReferenceEquals(activePlay, cardPlay)
                ? PlayScopes.GetOrCreateState(cardPlay, owner, factory)
                : null;
    }

    public static bool TryGetLatestPlayState<TState>(CardModel card, object owner, out TState? state)
        where TState : class
    {
        if (CardScopes.TryGetLatestScope(card, out CardResolution? resolution)
            && resolution is not null
            && PlayScopes.TryGetLatestScope(resolution, out CardPlay? cardPlay)
            && cardPlay is not null)
        {
            return PlayScopes.TryGetState(cardPlay, owner, out state);
        }

        state = null;
        return false;
    }

    public static TState? GetOrCreateCardState<TState>(CardModel card, object owner, Func<TState> factory)
        where TState : class
    {
        return CardScopes.TryGetLatestScope(card, out CardResolution? resolution) && resolution is not null
            ? CardScopes.GetOrCreateState(resolution, owner, factory)
            : null;
    }

    public static bool TryGetCardState<TState>(CardModel card, object owner, out TState? state)
        where TState : class
    {
        if (CardScopes.TryGetLatestScope(card, out CardResolution? resolution) && resolution is not null)
        {
            return CardScopes.TryGetState(resolution, owner, out state);
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
            PlayScopes.Complete(cardPlay);
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
            PlayScopes.CompleteSubject(resolution);
            CardScopes.Complete(resolution);
        }
    }

    internal sealed class CardResolution(CardModel card)
    {
        public CardModel Card { get; } = card;
    }
}
