using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Code.Interop;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Code.Commands;

public static class ScryCmd
{
    public static async Task Execute(PlayerChoiceContext choiceContext, Player player, int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        CardPile drawPile = PileType.Draw.GetPile(player);
        List<CardModel> cardsToScry = drawPile.Cards
            .Where(card => !PrepareCmd.ShouldReserveFromNormalDraw(card))
            .Take(amount)
            .ToList();
        if (cardsToScry.Count == 0)
        {
            return;
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

        foreach (CardModel card in cardsToDiscard)
        {
            await CardCmd.Discard(choiceContext, card);
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
    }
}
