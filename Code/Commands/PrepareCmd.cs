using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Afflictions;
using NinjaSlayer.Code.Prepared;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.Commands;

internal static class PrepareCmd
{
    public static bool CanPrepare(CardModel card)
    {
        return card.IsMutable
            && card.IsInCombat
            && !card.HasBeenRemovedFromState
            && card.Owner?.PlayerCombatState is not null
            && ModelDb.Affliction<PreparedAffliction>().CanAfflict(card);
    }

    public static bool IsPrepared(CardModel card) => card.Affliction is PreparedAffliction;

    public static bool ShouldReserveFromNormalDraw(CardModel card) => IsPrepared(card);

    public static async Task Apply(CardModel card)
    {
        if (!CanPrepare(card))
        {
            return;
        }

        CardPile drawPile = PileType.Draw.GetPile(card.Owner);
        int preparedAhead = drawPile.Cards.Count(IsPrepared);
        PreparedAffliction? affliction = await CardCmd.Afflict<PreparedAffliction>(card, 1m);
        if (affliction is null)
        {
            return;
        }

        CardPileAddResult result = await CardPileCmd.Add(card, drawPile, CardPilePosition.Top);
        if (!result.success || !drawPile.Cards.Any(candidate => ReferenceEquals(candidate, card)))
        {
            Repair(card, "draw-pile add was not confirmed", null);
            return;
        }

        // Top insertion is LIFO; place the new card after the existing prepared queue.
        bool repositioned = TryReposition(
            drawPile,
            card,
            Math.Min(preparedAhead, drawPile.Cards.Count),
            out Exception? repositionError);
        if (repositioned)
        {
            return;
        }

        if (PreparedSafetyService.HasStablePreparedPlacement(card, drawPile))
        {
            Entry.Logger.Warn($"Prepared queue reposition failed for {card.Id}; the card remains safely prepared. {repositionError}");
            return;
        }

        Repair(card, "queue reposition was not confirmed", repositionError);
    }

    private static bool TryReposition(
        CardPile pile,
        CardModel card,
        int index,
        out Exception? error)
    {
        int originalIndex = FindCardIndex(pile.Cards, card);
        if (originalIndex < 0)
        {
            error = new InvalidOperationException($"Prepared queue does not contain {card}.");
            return false;
        }

        try
        {
            pile.RemoveInternal(card, false);
            pile.AddInternal(card, Math.Clamp(index, 0, pile.Cards.Count), false);
            if (ContainsReference(pile.Cards, card))
            {
                error = null;
                return true;
            }

            error = new InvalidOperationException(
                "Prepared queue insert completed without retaining the card.");
        }
        catch (Exception exception)
        {
            error = exception;
        }

        if (ContainsReference(pile.Cards, card))
        {
            return false;
        }

        try
        {
            pile.AddInternal(card, Math.Clamp(originalIndex, 0, pile.Cards.Count), false);
        }
        catch (Exception rollbackFailure)
        {
            error = new AggregateException(error!, rollbackFailure);
        }

        if (!ContainsReference(pile.Cards, card))
        {
            error = new AggregateException(
                error!,
                new InvalidOperationException("Prepared queue rollback did not restore the card."));
        }

        return false;
    }

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

    private static bool ContainsReference(IReadOnlyList<CardModel> cards, CardModel card) =>
        FindCardIndex(cards, card) >= 0;

    private static void Repair(CardModel card, string reason, Exception? failure)
    {
        Exception? cleanupFailure = PreparedSafetyService.RepairAfterApplyFailure(card, reason);
        if (cleanupFailure is null)
        {
            Entry.Logger.Warn($"Prepared apply failed for {card.Id}: {reason}. {failure}");
            return;
        }

        Exception error = failure is null
            ? cleanupFailure
            : new AggregateException(failure, cleanupFailure);
        Entry.Logger.Error($"Prepared apply cleanup failed for {card.Id}: {reason}. {error}");
    }
}
