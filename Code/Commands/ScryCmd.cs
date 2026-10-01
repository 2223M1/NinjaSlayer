using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Code.Interop;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Code.Commands;

public static class ScryCmd
{
    internal const string SelectionPromptKey = "NINJA_SLAYER_SCRY_DISCARD";

    public static async Task<ScryResult> Execute(
        PlayerChoiceContext choiceContext,
        Player player,
        int amount,
        bool exhaustDiscarded = false)
    {
        if (amount <= 0)
        {
            return default;
        }

        CardPile drawPile = PileType.Draw.GetPile(player);
        List<CardModel> cardsToScry = drawPile.Cards
            .Take(amount)
            .ToList();
        if (cardsToScry.Count == 0)
        {
            return default;
        }

        var prefs = new CardSelectorPrefs(
            new LocString("card_selection", SelectionPromptKey),
            0,
            cardsToScry.Count
        );

        List<CardModel> cardsToDiscard = (await CardSelectCmd.FromSimpleGrid(
            choiceContext,
            cardsToScry,
            player,
            prefs
        )).ToList();

        int exhaustedCards = 0;
        int discardedAmount = 0;
        if (player.Creature.GetPower<ForethoughtPower>() is { } planning)
        {
            planning.ApplyToUnselectedCards(cardsToScry.Except(cardsToDiscard));
        }

        if (exhaustDiscarded)
        {
            foreach (CardModel card in cardsToDiscard)
            {
                await CardCmd.Exhaust(choiceContext, card);
                if (card.Pile?.Type == PileType.Exhaust)
                    exhaustedCards++;
            }
        }
        else
        {
            // Move the batch out before discard callbacks can draw it again. Keep native
            // pile notifications: skipVisuals also suppresses draw-pile counter updates.
            await CardPileCmd.Add(cardsToDiscard, PileType.Play);
            int historyStart = CombatManager.Instance.History.Entries.Count();
            await CardCmd.Discard(choiceContext, cardsToDiscard);
            discardedAmount = CombatManager.Instance.History.Entries.Skip(historyStart).OfType<CardDiscardedEntry>()
                .Select(entry => entry.Card).Intersect(cardsToDiscard).Count();
            Telemetry.NinjaSlayerCombatTelemetry.Mechanic("scry_discard", player.Creature, discardedAmount);
        }

        int viewedAmount = cardsToScry.Count;
        foreach (IScryListener listener in player.Creature.Powers.OfType<IScryListener>().ToList())
        {
            await listener.AfterScry(choiceContext, viewedAmount, discardedAmount);
        }

        if (WatcherScryHookInterop.IsReady)
        {
            await WatcherScryHookInterop.OnScryed(choiceContext, player, viewedAmount, discardedAmount);
        }

        return new ScryResult(viewedAmount, discardedAmount, exhaustedCards);
    }
}

public readonly record struct ScryResult(int Viewed, int Discarded, int ExhaustedCards);
