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

    public static async Task<PreparedApplyResult> Apply(CardModel card)
    {
        if (!CanPrepare(card))
        {
            return new PreparedApplyResult(PreparedApplyStatus.NotApplied);
        }

        CardPile drawPile = PileType.Draw.GetPile(card.Owner);
        int preparedAhead = drawPile.Cards.Count(IsPrepared);
        PreparedAffliction? affliction = await CardCmd.Afflict<PreparedAffliction>(card, 1m);
        if (affliction is null)
        {
            return new PreparedApplyResult(PreparedApplyStatus.NotApplied);
        }

        CardPileAddResult result = await CardPileCmd.Add(card, drawPile, CardPilePosition.Top);
        if (!result.success || !drawPile.Cards.Any(candidate => ReferenceEquals(candidate, card)))
        {
            PreparedCleanupResult cleanup = PreparedSafetyService.RepairAfterApplyFailure(
                card,
                "draw-pile add was not confirmed");
            return Report(card, PreparedApplyPolicy.ResolveAfterReposition(
                new PreparedQueueTransactionResult(
                    PreparedQueueTransactionStatus.FailedUncertain,
                    new InvalidOperationException("Prepared draw-pile add was not confirmed.")),
                hasStablePreparedPlacement: false,
                cleanup,
                "draw-pile add was not confirmed"));
        }

        // Top insertion is LIFO; place the new card after the existing prepared queue.
        PreparedQueueTransactionResult reposition = PreparedQueueCompatibility.TryReposition(
            drawPile,
            card,
            Math.Min(preparedAhead, drawPile.Cards.Count));
        bool stablePlacement = PreparedSafetyService.HasStablePreparedPlacement(card, drawPile);
        PreparedCleanupResult repair = stablePlacement
            ? new PreparedCleanupResult(PreparedCleanupStatus.NotRequired)
            : PreparedSafetyService.RepairAfterApplyFailure(card, "queue reposition was not confirmed");
        return Report(card, PreparedApplyPolicy.ResolveAfterReposition(
            reposition,
            stablePlacement,
            repair,
            "queue reposition was not confirmed"));
    }

    private static PreparedApplyResult Report(CardModel card, PreparedApplyResult result)
    {
        if (!result.IsDegraded)
        {
            return result;
        }

        string diagnostic = result.Error is null ? string.Empty : $" {result.Error}";
        string message = $"Prepared apply {result.Status} for {card.Id}: {result.Reason}.{diagnostic}";
        if (result.RequiresLifecycleRepair)
        {
            Entry.Logger.Error(message);
        }
        else
        {
            Entry.Logger.Warn(message);
        }

        return result;
    }
}
