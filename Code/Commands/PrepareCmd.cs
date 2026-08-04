using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Afflictions;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Code.Prepared;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.Commands;

internal static class PrepareCmd
{
    public static bool CanPrepare(CardModel card)
    {
        return NinjaSlayerPatchCapabilities.PreparedGameplayEnabled
            && card.IsMutable
            && card.IsInCombat
            && !card.HasBeenRemovedFromState
            && card.Owner?.PlayerCombatState is not null
            && ModelDb.Affliction<PreparedAffliction>().CanAfflict(card);
    }

    public static bool IsPrepared(CardModel card) => card.Affliction is PreparedAffliction;

    public static bool ShouldReserveFromNormalDraw(CardModel card) =>
        NinjaSlayerPatchCapabilities.PreparedGameplayEnabled && IsPrepared(card);

    public static async Task Apply(CardModel card)
    {
        if (!CanPrepare(card))
        {
            return;
        }

        CardPile drawPile = PileType.Draw.GetPile(card.Owner);
        int preparedAhead = drawPile.Cards.Count(IsPrepared);
        PreparedAffliction? affliction = await CardCmd.Afflict<PreparedAffliction>(card, 1m);
        if (affliction is null)
        {
            return;
        }

        CardPileAddResult result = await CardPileCmd.Add(card, drawPile, CardPilePosition.Top);
        if (!result.success || !drawPile.Cards.Any(candidate => ReferenceEquals(candidate, card)))
        {
            Repair(card, "draw-pile add was not confirmed", null);
            return;
        }

        // Top insertion is LIFO; place the new card after the existing prepared queue.
        bool repositioned = PreparedQueueCompatibility.TryReposition(
            drawPile,
            card,
            Math.Min(preparedAhead, drawPile.Cards.Count),
            out Exception? repositionError);
        if (repositioned)
        {
            return;
        }

        if (PreparedSafetyService.HasStablePreparedPlacement(card, drawPile))
        {
            Entry.Logger.Warn($"Prepared queue reposition failed for {card.Id}; the card remains safely prepared. {repositionError}");
            return;
        }

        Repair(card, "queue reposition was not confirmed", repositionError);
    }

    private static void Repair(CardModel card, string reason, Exception? failure)
    {
        Exception? cleanupFailure = PreparedSafetyService.RepairAfterApplyFailure(card, reason);
        if (cleanupFailure is null)
        {
            Entry.Logger.Warn($"Prepared apply failed for {card.Id}: {reason}. {failure}");
            return;
        }

        Exception error = failure is null
            ? cleanupFailure
            : new AggregateException(failure, cleanupFailure);
        Entry.Logger.Error($"Prepared apply cleanup failed for {card.Id}: {reason}. {error}");
    }
}
