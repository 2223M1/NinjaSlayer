using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Afflictions;

namespace NinjaSlayer.Code.Commands;

internal static class PrepareCmd
{
    public static bool CanPrepare(CardModel card)
    {
        return card.IsMutable
            && card.IsInCombat
            && !card.HasBeenRemovedFromState
            && card.Owner?.PlayerCombatState is not null
            && card.Affliction is null
            && ModelDb.Affliction<PreparedAffliction>().CanAfflict(card);
    }

    public static bool IsPrepared(CardModel card) => card.Affliction is PreparedAffliction;

    public static bool ShouldReserveFromNormalDraw(CardModel card) => IsPrepared(card);

    public static async Task<bool> Apply(CardModel card)
    {
        if (!CanPrepare(card))
        {
            return false;
        }

        PreparedSnapshot snapshot = CaptureSnapshot(card);
        PreparedAffliction? installedAffliction = null;
        try
        {
            installedAffliction = await CardCmd.Afflict<PreparedAffliction>(card, 1m);
            if (installedAffliction is null)
            {
                ValidateRestoredState(card, snapshot);
                return false;
            }

            ValidateOwnedAffliction(card, installedAffliction);

            CardPileAddResult result = await CardPileCmd.Add(
                card,
                snapshot.DrawPile,
                CardPilePosition.Top);
            if (!result.success
                || !ReferenceEquals(result.cardAdded, card)
                || !ReferenceEquals(result.oldPile, snapshot.OriginalPile))
            {
                throw new InvalidOperationException("Draw-pile add was not confirmed.");
            }

            RepositionAfterPreparedQueue(snapshot.DrawPile, card, snapshot.PreparedQueue.Count);
            ValidatePreparedState(card, snapshot, installedAffliction);
            return true;
        }
        catch (Exception primaryFailure)
        {
            Exception? rollbackFailure = Rollback(card, snapshot, installedAffliction);
            if (rollbackFailure is null)
            {
                throw;
            }

            throw new AggregateException(
                "Prepared transaction and rollback both failed.",
                primaryFailure,
                rollbackFailure);
        }
    }

    private static PreparedSnapshot CaptureSnapshot(CardModel card)
    {
        Player owner = card.Owner
            ?? throw new InvalidOperationException("Prepared card has no owner.");
        ICombatState combatState = card.CombatState
            ?? throw new InvalidOperationException("Prepared card has no combat state.");
        if (!ReferenceEquals(owner.Creature.CombatState, combatState)
            || owner.PlayerCombatState?.AllCards.Contains(card) != true)
        {
            throw new InvalidOperationException("Prepared card does not belong to its owner's active combat.");
        }

        CardPile originalPile = card.Pile
            ?? throw new InvalidOperationException("Prepared card is not in a pile.");
        int originalIndex = FindCardIndex(originalPile.Cards, card);
        if (originalIndex < 0 || CountOccurrences(owner, card) != 1)
        {
            throw new InvalidOperationException("Prepared card does not have exactly one pile reference.");
        }

        CardPile drawPile = PileType.Draw.GetPile(owner);
        List<PreparedQueueEntry> preparedQueue = CapturePreparedQueue(
            owner,
            combatState,
            drawPile);
        return new PreparedSnapshot(
            owner,
            combatState,
            originalPile,
            originalIndex,
            drawPile,
            preparedQueue);
    }

    private static List<PreparedQueueEntry> CapturePreparedQueue(
        Player owner,
        ICombatState combatState,
        CardPile drawPile)
    {
        List<PreparedQueueEntry> queue = [];
        bool sawUnpreparedCard = false;
        foreach (CardModel candidate in drawPile.Cards)
        {
            if (candidate.Affliction is not PreparedAffliction affliction)
            {
                sawUnpreparedCard = true;
                continue;
            }

            if (sawUnpreparedCard)
            {
                throw new InvalidOperationException(
                    "Prepared cards do not form the draw-pile queue prefix.");
            }

            if (queue.Any(queued => ReferenceEquals(queued.Card, candidate)))
            {
                throw new InvalidOperationException("Prepared queue contains a duplicate card reference.");
            }

            queue.Add(new PreparedQueueEntry(candidate, affliction));
        }

        foreach (PreparedQueueEntry queued in queue)
        {
            ValidateQueueCardOwnership(owner, combatState, drawPile, queued.Card);
        }

        if (owner.Piles.Any(pile =>
                !ReferenceEquals(pile, drawPile)
                && pile.Cards.Any(IsPrepared)))
        {
            throw new InvalidOperationException("Prepared card exists outside the draw pile.");
        }

        return queue;
    }

    private static void RepositionAfterPreparedQueue(CardPile drawPile, CardModel card, int index)
    {
        int currentIndex = FindCardIndex(drawPile.Cards, card);
        if (currentIndex < 0)
        {
            throw new InvalidOperationException("Prepared card disappeared before queue positioning.");
        }

        if (currentIndex == index)
        {
            return;
        }

        drawPile.RemoveInternal(card, silent: true);
        drawPile.AddInternal(card, index, silent: true);
        drawPile.InvokeContentsChanged();
    }

    private static void ValidatePreparedState(
        CardModel card,
        PreparedSnapshot snapshot,
        PreparedAffliction installedAffliction)
    {
        ValidateOwnership(card, snapshot, snapshot.DrawPile, snapshot.PreparedQueue.Count);
        ValidateOwnedAffliction(card, installedAffliction);
        ValidatePreparedQueue(
            snapshot,
            appendedCard: card,
            appendedAffliction: installedAffliction);
    }

    private static void ValidateRestoredState(CardModel card, PreparedSnapshot snapshot)
    {
        ValidateOwnership(card, snapshot, snapshot.OriginalPile, snapshot.OriginalIndex);
        if (card.Affliction is not null)
        {
            throw new InvalidOperationException("Prepared rollback did not restore the original affliction.");
        }

        ValidatePreparedQueue(
            snapshot,
            appendedCard: null,
            appendedAffliction: null);
    }

    private static void ValidateOwnedAffliction(
        CardModel card,
        PreparedAffliction installedAffliction)
    {
        if (!ReferenceEquals(card.Affliction, installedAffliction))
        {
            throw new InvalidOperationException(
                "Prepared transaction lost ownership of its affliction.");
        }
    }

    private static void ValidateOwnership(
        CardModel card,
        PreparedSnapshot snapshot,
        CardPile expectedPile,
        int expectedIndex)
    {
        if (!ReferenceEquals(card.Owner, snapshot.Owner)
            || !ReferenceEquals(card.CombatState, snapshot.CombatState)
            || !ReferenceEquals(snapshot.Owner.Creature.CombatState, snapshot.CombatState)
            || snapshot.Owner.PlayerCombatState?.AllCards.Contains(card) != true
            || !card.IsInCombat
            || card.HasBeenRemovedFromState)
        {
            throw new InvalidOperationException("Prepared transaction changed combat ownership.");
        }

        if (CountOccurrences(snapshot.Owner, card) != 1
            || !ReferenceEquals(card.Pile, expectedPile)
            || FindCardIndex(expectedPile.Cards, card) != expectedIndex)
        {
            throw new InvalidOperationException(
                "Prepared transaction did not preserve unique pile ownership and position.");
        }
    }

    private static void ValidatePreparedQueue(
        PreparedSnapshot snapshot,
        CardModel? appendedCard,
        PreparedAffliction? appendedAffliction)
    {
        Player owner = snapshot.Owner;
        CardPile drawPile = snapshot.DrawPile;
        int expectedCount = snapshot.PreparedQueue.Count + (appendedCard is null ? 0 : 1);
        if (drawPile.Cards.Count < expectedCount)
        {
            throw new InvalidOperationException("Prepared queue is shorter than expected.");
        }

        for (int index = 0; index < snapshot.PreparedQueue.Count; index++)
        {
            PreparedQueueEntry expected = snapshot.PreparedQueue[index];
            if (!ReferenceEquals(drawPile.Cards[index], expected.Card))
            {
                throw new InvalidOperationException("Prepared queue order changed.");
            }

            ValidateQueueCardOwnership(
                owner,
                snapshot.CombatState,
                drawPile,
                expected.Card);
            if (!ReferenceEquals(expected.Card.Affliction, expected.Affliction))
            {
                throw new InvalidOperationException(
                    "Prepared queue card lost affliction ownership.");
            }
        }

        if (appendedCard is not null)
        {
            if (appendedAffliction is null
                || !ReferenceEquals(drawPile.Cards[snapshot.PreparedQueue.Count], appendedCard))
            {
                throw new InvalidOperationException("New Prepared card was not appended to the queue.");
            }

            ValidateQueueCardOwnership(
                owner,
                snapshot.CombatState,
                drawPile,
                appendedCard);
            if (!ReferenceEquals(appendedCard.Affliction, appendedAffliction))
            {
                throw new InvalidOperationException(
                    "New Prepared card lost affliction ownership.");
            }
        }

        for (int index = expectedCount; index < drawPile.Cards.Count; index++)
        {
            if (IsPrepared(drawPile.Cards[index]))
            {
                throw new InvalidOperationException("Prepared card exists outside the queue prefix.");
            }
        }

        if (owner.Piles.Any(pile =>
                !ReferenceEquals(pile, drawPile)
                && pile.Cards.Any(IsPrepared)))
        {
            throw new InvalidOperationException("Prepared card exists outside the draw pile.");
        }
    }

    private static void ValidateQueueCardOwnership(
        Player owner,
        ICombatState combatState,
        CardPile drawPile,
        CardModel card)
    {
        if (!ReferenceEquals(card.Owner, owner)
            || !ReferenceEquals(card.CombatState, combatState)
            || !ReferenceEquals(owner.Creature.CombatState, combatState)
            || owner.PlayerCombatState?.AllCards.Contains(card) != true
            || !card.IsInCombat
            || card.HasBeenRemovedFromState
            || !ReferenceEquals(card.Pile, drawPile)
            || CountOccurrences(owner, card) != 1)
        {
            throw new InvalidOperationException(
                "Prepared queue card lost active-combat or unique pile ownership.");
        }
    }

    private static Exception? Rollback(
        CardModel card,
        PreparedSnapshot snapshot,
        PreparedAffliction? installedAffliction)
    {
        List<Exception> failures = [];
        HashSet<CardPile> touchedPiles = new(ReferenceEqualityComparer.Instance);
        bool pileAlreadyRestored = CountOccurrences(snapshot.Owner, card) == 1
            && ReferenceEquals(card.Pile, snapshot.OriginalPile)
            && FindCardIndex(snapshot.OriginalPile.Cards, card) == snapshot.OriginalIndex;
        if (!pileAlreadyRestored)
        {
            foreach (CardPile pile in snapshot.Owner.Piles)
            {
                while (CountReferences(pile.Cards, card) > 0)
                {
                    touchedPiles.Add(pile);
                    int before = CountReferences(pile.Cards, card);
                    try
                    {
                        pile.RemoveInternal(card, silent: true);
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                    }

                    if (CountReferences(pile.Cards, card) >= before)
                    {
                        break;
                    }
                }
            }

            if (CountOccurrences(snapshot.Owner, card) == 0)
            {
                try
                {
                    snapshot.OriginalPile.AddInternal(
                        card,
                        Math.Clamp(snapshot.OriginalIndex, 0, snapshot.OriginalPile.Cards.Count),
                        silent: true);
                    touchedPiles.Add(snapshot.OriginalPile);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }
        }

        if (installedAffliction is not null
            && ReferenceEquals(card.Affliction, installedAffliction))
        {
            try
            {
                CardCmd.ClearAffliction(card);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
        else if (card.Affliction is not null)
        {
            failures.Add(new InvalidOperationException(
                "Prepared rollback refused to clear an affliction it does not own."));
        }

        foreach (CardPile pile in touchedPiles)
        {
            try
            {
                pile.InvokeContentsChanged();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        try
        {
            ValidateRestoredState(card, snapshot);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        return failures.Count switch
        {
            0 => null,
            1 => failures[0],
            _ => new AggregateException("Prepared rollback failed.", failures)
        };
    }

    private static int CountOccurrences(Player owner, CardModel card) =>
        owner.Piles.Sum(pile => CountReferences(pile.Cards, card));

    private static int CountReferences(IReadOnlyList<CardModel> cards, CardModel card) =>
        cards.Count(candidate => ReferenceEquals(candidate, card));

    private static int FindCardIndex(IReadOnlyList<CardModel> cards, CardModel card)
    {
        for (int index = 0; index < cards.Count; index++)
        {
            if (ReferenceEquals(cards[index], card))
            {
                return index;
            }
        }

        return -1;
    }

    private sealed record PreparedQueueEntry(
        CardModel Card,
        PreparedAffliction Affliction);

    private sealed record PreparedSnapshot(
        Player Owner,
        ICombatState CombatState,
        CardPile OriginalPile,
        int OriginalIndex,
        CardPile DrawPile,
        IReadOnlyList<PreparedQueueEntry> PreparedQueue);
}
