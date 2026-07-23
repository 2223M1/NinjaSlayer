using System.Reflection;
using System.Runtime.ExceptionServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Code.Prepared;

namespace NinjaSlayer.Code.Compatibility;

internal static class PreparedQueueCompatibility
{
    private const string ExpectedAssemblyVersion = "0.1.0.0";
    private const string ExpectedModuleMvid = "a49d3537-5a42-4dcd-9877-663e394f2b44";
    private const int ExpectedAddMetadataToken = 0x060084B5;
    private const string ExpectedAddIlSha256 = "95bd1d75151afad1eea28fae26d0d99404f8cea672f186f2206fd96610524d37";
    private const int ExpectedRemoveMetadataToken = 0x060084B6;
    private const string ExpectedRemoveIlSha256 = "7ea0ab7d874440c7901b965c2c7c534f38a328197728bdda1bf2da232b663b3d";

    private static readonly MethodInfo? AddInternal = AccessTools.Method(
        typeof(CardPile),
        nameof(CardPile.AddInternal),
        [typeof(CardModel), typeof(int), typeof(bool)]);
    private static readonly MethodInfo? RemoveInternal = AccessTools.Method(
        typeof(CardPile),
        nameof(CardPile.RemoveInternal),
        [typeof(CardModel), typeof(bool)]);

    public static bool TryValidate(out PreparedQueueFingerprint fingerprint, out string reason)
    {
        if (!PreparedMethodContract.TryCapture(AddInternal, out PreparedMethodFingerprint add, out reason))
        {
            fingerprint = default;
            return false;
        }
        if (!PreparedMethodContract.TryCapture(RemoveInternal, out PreparedMethodFingerprint remove, out reason))
        {
            fingerprint = default;
            return false;
        }

        fingerprint = new PreparedQueueFingerprint(add, remove);
        if (!PreparedMethodContract.Matches(
                add,
                ExpectedAssemblyVersion,
                ExpectedModuleMvid,
                ExpectedAddMetadataToken,
                ExpectedAddIlSha256))
        {
            reason = $"CardPile.AddInternal fingerprint mismatch ({add}).";
            return false;
        }
        if (!PreparedMethodContract.Matches(
                remove,
                ExpectedAssemblyVersion,
                ExpectedModuleMvid,
                ExpectedRemoveMetadataToken,
                ExpectedRemoveIlSha256))
        {
            reason = $"CardPile.RemoveInternal fingerprint mismatch ({remove}).";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public static PreparedQueueTransactionResult TryReposition(CardPile pile, CardModel card, int index)
    {
        if (AddInternal is null || RemoveInternal is null)
        {
            return new PreparedQueueTransactionResult(
                ContainsReference(pile.Cards, card)
                    ? PreparedQueueTransactionStatus.FailedStable
                    : PreparedQueueTransactionStatus.FailedUncertain,
                new MissingMethodException("Prepared queue methods are unavailable."));
        }

        int originalIndex = FindCardIndex(pile.Cards, card);
        if (originalIndex < 0)
        {
            return new PreparedQueueTransactionResult(
                PreparedQueueTransactionStatus.FailedUncertain,
                new InvalidOperationException($"Prepared queue does not contain {card}."));
        }

        return PreparedQueueTransaction.Execute(
            remove: () => Invoke(RemoveInternal, pile, [card, false]),
            insertAtTarget: () => Invoke(
                AddInternal,
                pile,
                [card, Math.Clamp(index, 0, pile.Cards.Count), false]),
            restoreAtOriginal: () => Invoke(
                AddInternal,
                pile,
                [card, Math.Clamp(originalIndex, 0, pile.Cards.Count), false]),
            isPresent: () => ContainsReference(pile.Cards, card));
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

internal readonly record struct PreparedQueueFingerprint(
    PreparedMethodFingerprint AddInternal,
    PreparedMethodFingerprint RemoveInternal)
{
    public override string ToString() => $"add=[{AddInternal}], remove=[{RemoveInternal}]";
}
