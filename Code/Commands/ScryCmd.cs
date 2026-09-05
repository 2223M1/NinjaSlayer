using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Code.Interop;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Code.Commands;

public static class ScryCmd
{
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
            .Where(card => !PrepareCmd.ShouldReserveFromNormalDraw(card))
            .Take(amount)
            .ToList();
        if (cardsToScry.Count == 0)
        {
            return default;
        }

        var prefs = new CardSelectorPrefs(
            CardSelectorPrefs.DiscardSelectionPrompt,
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
        foreach (CardModel card in cardsToDiscard)
        {
            if (card is ReflexGuardRedesignV1)
            {
                await CardCmd.AutoPlay(choiceContext, card, null);
            }
            else if (card.Type == CardType.Status
                     && player.Creature.HasPower<ScryStatusExhaustPower>())
            {
                await CardCmd.Exhaust(choiceContext, card);
                exhaustedCards++;
            }
            else if (exhaustDiscarded)
            {
                await CardCmd.Exhaust(choiceContext, card);
                exhaustedCards++;
            }
            else
            {
                await CardCmd.Discard(choiceContext, card);
            }
        }

        int discardedAmount = cardsToDiscard.Count;
        int viewedAmount = cardsToScry.Count;
        foreach (IRedesignScryListener listener in player.Creature.Powers.OfType<IRedesignScryListener>().ToList())
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
