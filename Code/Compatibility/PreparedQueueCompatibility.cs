using System.Reflection;
using System.Runtime.ExceptionServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Code.Prepared;

namespace NinjaSlayer.Code.Compatibility;

internal static class PreparedQueueCompatibility
{
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
        if (!MethodBodyFingerprintCapture.TryCapture(
                AddInternal,
                out MethodBodyFingerprint add,
                out reason))
        {
            fingerprint = default;
            return false;
        }
        if (!MethodBodyFingerprintCapture.TryCapture(
                RemoveInternal,
                out MethodBodyFingerprint remove,
                out reason))
        {
            fingerprint = default;
            return false;
        }

        if (!GameHostContractProfile.TryResolve(add, out GameHostContractProfile profile))
        {
            fingerprint = default;
            reason = $"Unsupported CardPile.AddInternal host ({add}).";
            return false;
        }
        if (!StableMethodBodyContract.Matches(add, profile, profile.PreparedQueueAdd))
        {
            fingerprint = default;
            reason = $"CardPile.AddInternal fingerprint mismatch for {profile.Id} ({add}).";
            return false;
        }
        if (!StableMethodBodyContract.Matches(remove, profile, profile.PreparedQueueRemove))
        {
            fingerprint = default;
            reason = $"CardPile.RemoveInternal fingerprint mismatch for {profile.Id} ({remove}).";
            return false;
        }

        fingerprint = new PreparedQueueFingerprint(profile.Id, add, remove);
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
    string HostProfile,
    MethodBodyFingerprint AddInternal,
    MethodBodyFingerprint RemoveInternal)
{
    public override string ToString() =>
        $"host={HostProfile}, add=[{AddInternal}], remove=[{RemoveInternal}]";
}
