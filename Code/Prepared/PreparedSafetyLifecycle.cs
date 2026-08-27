using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Code.Commands;
using STS2RitsuLib;

namespace NinjaSlayer.Code.Prepared;

internal sealed class PreparedSafetyLifecycle : IDisposable
{
    private readonly IDisposable _subscription;
    private bool _disposed;

    private PreparedSafetyLifecycle(IDisposable subscription)
    {
        _subscription = subscription;
    }

    public static PreparedSafetyLifecycle Subscribe() =>
        new(RitsuLibFramework.SubscribeLifecycle<CardMovedBetweenPilesEvent>(
            CompletePileChange,
            replayCurrentState: false));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _subscription.Dispose();
    }

    private static void CompletePileChange(CardMovedBetweenPilesEvent evt)
    {
        CardModel card = evt.Card;
        if (evt.PreviousPile != PileType.Draw
            || card.Pile?.Type == PileType.Draw
            || !PrepareCmd.IsPrepared(card))
        {
            return;
        }

        CardCmd.ClearAffliction(card);
        if (PrepareCmd.IsPrepared(card))
        {
            throw new InvalidOperationException(
                $"Prepared affliction remained after {card.Id} left the draw pile.");
        }
    }
}
