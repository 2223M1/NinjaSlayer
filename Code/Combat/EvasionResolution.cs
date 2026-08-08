using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Code.Lifecycle;

namespace NinjaSlayer.Code.Combat;

internal static class EvasionResolution
{
    private static readonly object ScopeOwner = new();
    private static readonly AsyncLocal<MoveFrame?> CurrentMove = new();

    public static MoveFrame EnterMove()
    {
        MoveFrame frame = new(CurrentMove.Value);
        CurrentMove.Value = frame;
        return frame;
    }

    public static void RestoreCaller(MoveFrame frame)
    {
        if (ReferenceEquals(CurrentMove.Value, frame))
        {
            CurrentMove.Value = frame.Previous;
        }
    }

    public static async Task CompleteMove(Task task, MoveFrame frame)
    {
        try
        {
            await task;
        }
        finally
        {
            frame.IsActive = false;
        }
    }

    public static void RecordEvadedHit(
        CardModel? cardSource,
        Creature attacker,
        Creature target) =>
        RecordHit(cardSource, attacker, target, evaded: true);

    public static void RecordConnectedHit(
        CardModel? cardSource,
        Creature attacker,
        Creature target) =>
        RecordHit(cardSource, attacker, target, evaded: false);

    public static bool ShouldSuppressDebuff(
        CardModel? cardSource,
        Creature? applier,
        Creature target,
        PowerModel power,
        decimal amount)
    {
        if (applier is null || power.GetTypeForAmount(amount) != PowerType.Debuff)
        {
            return false;
        }

        if (cardSource?.Type == CardType.Attack)
        {
            return CardPlayResolutionScope.TryGetLatestPlayState(
                    cardSource,
                    ScopeOwner,
                    out EvasionHitLedger? ledger)
                && ledger.WasOnlyEvaded(applier, target);
        }

        return cardSource is null
            && CurrentMove.Value is { IsActive: true } frame
            && frame.Ledger.WasOnlyEvaded(applier, target);
    }

    private static void RecordHit(
        CardModel? cardSource,
        Creature attacker,
        Creature target,
        bool evaded)
    {
        EvasionHitLedger? ledger;
        if (cardSource?.Type == CardType.Attack)
        {
            if (!CardPlayResolutionScope.TryResolveCurrentPlay(cardSource, out CardPlay? cardPlay))
            {
                return;
            }

            ledger = CardPlayResolutionScope.GetOrCreatePlayState(
                cardPlay,
                ScopeOwner,
                static () => new EvasionHitLedger());
        }
        else if (cardSource is null)
        {
            ledger = CurrentMove.Value is { IsActive: true } frame ? frame.Ledger : null;
        }
        else
        {
            ledger = null;
        }

        if (evaded)
        {
            ledger?.RecordEvaded(attacker, target);
        }
        else
        {
            ledger?.RecordConnected(attacker, target);
        }
    }

    internal sealed class MoveFrame(MoveFrame? previous)
    {
        public MoveFrame? Previous { get; } = previous;
        public EvasionHitLedger Ledger { get; } = new();
        public bool IsActive { get; set; } = true;
    }
}
