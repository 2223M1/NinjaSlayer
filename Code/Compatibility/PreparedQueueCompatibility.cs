using System.Reflection;
using System.Runtime.ExceptionServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace NinjaSlayer.Code.Compatibility;

internal static class PreparedQueueCompatibility
{
    private static readonly MethodInfo AddInternal = AccessTools.Method(
        typeof(CardPile),
        nameof(CardPile.AddInternal),
        [typeof(CardModel), typeof(int), typeof(bool)])
        ?? throw new MissingMethodException(typeof(CardPile).FullName, nameof(CardPile.AddInternal));
    private static readonly MethodInfo RemoveInternal = AccessTools.Method(
        typeof(CardPile),
        nameof(CardPile.RemoveInternal),
        [typeof(CardModel), typeof(bool)])
        ?? throw new MissingMethodException(typeof(CardPile).FullName, nameof(CardPile.RemoveInternal));

    public static bool TryReposition(
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
            Invoke(RemoveInternal, pile, [card, false]);
            Invoke(AddInternal, pile, [card, Math.Clamp(index, 0, pile.Cards.Count), false]);
            if (ContainsReference(pile.Cards, card))
            {
                error = null;
                return true;
            }

            error = new InvalidOperationException("Prepared queue insert completed without retaining the card.");
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
            Invoke(AddInternal, pile, [card, Math.Clamp(originalIndex, 0, pile.Cards.Count), false]);
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

    private static void Invoke(MethodInfo method, object instance, object?[] arguments)
    {
        try
        {
            method.Invoke(instance, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is { } inner)
        {
            ExceptionDispatchInfo.Capture(inner).Throw();
            throw;
        }
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
}
