using System.Reflection;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Code.Events;

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

        await NinjaSlayerHook.OnScryed(choiceContext, player, amount, cardsToDiscard.Count);
        await NotifyWatcherScryed(choiceContext, player, amount, cardsToDiscard.Count);
    }

    private static async Task NotifyWatcherScryed(PlayerChoiceContext choiceContext, Player player, int amount, int discardedAmount)
    {
        MethodInfo? onScryed = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("Watcher.Code.Events.WatcherHook"))
            .Where(t => t != null)
            .Select(t => t!.GetMethod(
                "OnScryed",
                BindingFlags.Public | BindingFlags.Static,
                null,
                [typeof(PlayerChoiceContext), typeof(Player), typeof(int), typeof(int)],
                null))
            .FirstOrDefault(m => m != null);

        if (onScryed?.Invoke(null, new object[] { choiceContext, player, amount, discardedAmount }) is Task task)
        {
            await task;
        }
    }
}
