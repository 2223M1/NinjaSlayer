using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Code.Events;
using NinjaSlayer.Code.Interop;

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
        List<CardModel> cardsToScry = drawPile.Cards.Take(amount).ToList();
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
        await NinjaSlayerHook.OnScryed(choiceContext, player, amount, discardedAmount);

        if (WatcherScryHookInterop.IsReady)
        {
            await WatcherScryHookInterop.OnScryed(choiceContext, player, amount, discardedAmount);
        }
    }
}
