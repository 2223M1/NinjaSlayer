using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.Prepared;

internal static class PreparedSafetyService
{
    public static async Task CompletePileChangeAfter(Task original, CardModel card, PileType oldPile)
    {
        await original;
        if (!NinjaSlayerPatchCapabilities.PreparedSafetyEnabled)
        {
            return;
        }

        ICombatState? currentCombat = CombatManager.Instance.DebugOnlyGetState();
        bool belongsToCurrentCombat = BelongsToCombat(card, currentCombat);
        if (PreparedSafetyPolicy.ShouldClearAfterPileChange(
                PrepareCmd.IsPrepared(card),
                oldPile == PileType.Draw,
                card.Pile?.Type == PileType.Draw,
                belongsToCurrentCombat))
        {
            TryClear(card, "confirmed draw-pile exit");
        }
    }

    public static void RecoverAfterRunLoaded(IRunState runState)
    {
        if (NinjaSlayerPatchCapabilities.PreparedSafetyEnabled)
        {
            CleanupPlayers(runState.Players, combatIsEnding: false, "run load");
        }
    }

    public static void RecoverBeforeCombatStart()
    {
        if (NinjaSlayerPatchCapabilities.PreparedSafetyEnabled
            && CombatManager.Instance.DebugOnlyGetState() is { } combatState)
        {
            CleanupPlayers(combatState.Players, combatIsEnding: false, "combat start");
        }
    }

    public static void RecoverAfterCombatEnd(CombatRoom room)
    {
        if (NinjaSlayerPatchCapabilities.PreparedSafetyEnabled)
        {
            CleanupPlayers(room.CombatState.Players, combatIsEnding: true, "combat end");
        }
    }

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
                if (PreparedSafetyPolicy.ShouldClearAtLifecycleBoundary(
                        PrepareCmd.IsPrepared(card),
                        card.Pile?.Type == PileType.Draw,
                        BelongsToCombat(card, combatState),
                        combatIsEnding))
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

    private static void TryClear(CardModel card, string reason)
    {
        try
        {
            CardCmd.ClearAffliction(card);
        }
        catch (Exception exception)
        {
            Entry.Logger.Error($"Prepared safety cleanup failed at {reason} for {card.Id}: {exception}");
        }
    }
}
