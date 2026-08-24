using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.Prepared;

internal static class PreparedSafetyService
{
    public static void CompletePileChange(
        ICombatState? suppliedCombatState,
        CardModel card,
        PileType oldPile)
    {
        ICombatState? cardCombatState = card.CombatState ?? card.Owner?.Creature.CombatState;
        if (suppliedCombatState is not null
            && cardCombatState is not null
            && !ReferenceEquals(suppliedCombatState, cardCombatState))
        {
            return;
        }

        ICombatState? combatState = suppliedCombatState ?? cardCombatState;
        if (combatState is not null
            && PrepareCmd.IsPrepared(card)
            && oldPile == PileType.Draw
            && card.Pile?.Type != PileType.Draw
            && BelongsToCombat(card, combatState))
        {
            _ = TryClear(card, "confirmed draw-pile exit");
        }
    }

    public static void RecoverAfterRunLoaded(IRunState runState) =>
        CleanupPlayers(runState.Players, combatIsEnding: false, "run load");

    public static void RecoverBeforeCombatStart(ICombatState? combatState)
    {
        if (combatState is not null)
        {
            CleanupPlayers(combatState.Players, combatIsEnding: false, "combat start");
        }
    }

    public static void RecoverAfterCombatEnd(CombatRoom room) =>
        CleanupPlayers(room.CombatState.Players, combatIsEnding: true, "combat end");

    public static bool HasStablePreparedPlacement(CardModel card, CardPile expectedDrawPile)
    {
        int occurrences = card.Owner.Piles.Sum(pile =>
            pile.Cards.Count(candidate => ReferenceEquals(candidate, card)));
        return PrepareCmd.IsPrepared(card)
            && ReferenceEquals(card.Pile, expectedDrawPile)
            && expectedDrawPile.Cards.Count(candidate => ReferenceEquals(candidate, card)) == 1
            && occurrences == 1
            && BelongsToCombat(card, card.Owner.Creature.CombatState);
    }

    public static Exception? RepairAfterApplyFailure(CardModel card, string reason) =>
        TryClear(card, $"apply recovery: {reason}", logFailure: false);

    private static void CleanupPlayers(
        IEnumerable<Player> players,
        bool combatIsEnding,
        string boundary)
    {
        foreach (Player player in players)
        {
            ICombatState? combatState = player.Creature.CombatState;
            var cards = new HashSet<CardModel>(ReferenceEqualityComparer.Instance);
            cards.UnionWith(player.Deck.Cards);
            if (player.PlayerCombatState is { } playerCombatState)
            {
                cards.UnionWith(playerCombatState.AllCards);
            }

            foreach (CardModel card in cards)
            {
                if (PrepareCmd.IsPrepared(card)
                    && (combatIsEnding
                        || card.Pile?.Type != PileType.Draw
                        || !BelongsToCombat(card, combatState)))
                {
                    TryClear(card, boundary);
                }
            }
        }
    }

    private static bool BelongsToCombat(CardModel card, ICombatState? combatState) =>
        combatState is not null
        && card.IsInCombat
        && !card.HasBeenRemovedFromState
        && ReferenceEquals(card.CombatState, combatState)
        && card.Owner.PlayerCombatState?.AllCards.Contains(card) == true;

    private static Exception? TryClear(CardModel card, string reason, bool logFailure = true)
    {
        if (!PrepareCmd.IsPrepared(card))
        {
            return null;
        }

        try
        {
            CardCmd.ClearAffliction(card);
            if (!PrepareCmd.IsPrepared(card))
            {
                return null;
            }

            var exception = new InvalidOperationException("Prepared affliction remained after cleanup.");
            LogCleanupFailure(card, reason, exception, logFailure);
            return exception;
        }
        catch (Exception exception)
        {
            LogCleanupFailure(card, reason, exception, logFailure);
            return exception;
        }
    }

    private static void LogCleanupFailure(
        CardModel card,
        string reason,
        Exception exception,
        bool logFailure)
    {
        if (logFailure)
        {
            Entry.Logger.Error($"Prepared safety cleanup failed at {reason} for {card.Id}: {exception}");
        }
    }
}
