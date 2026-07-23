using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Afflictions;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Code.Patches;

namespace NinjaSlayer.Code.Commands;

public static class PrepareCmd
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

    public static async Task<bool> Apply(CardModel card)
    {
        if (!CanPrepare(card))
        {
            return false;
        }

        CardPile drawPile = PileType.Draw.GetPile(card.Owner);
        int preparedAhead = drawPile.Cards.Count(IsPrepared);
        PreparedAffliction? affliction = await CardCmd.Afflict<PreparedAffliction>(card, 1m);
        if (affliction is null)
        {
            return false;
        }

        CardPileAddResult result = await CardPileCmd.Add(card, drawPile, CardPilePosition.Top);
        if (!result.success || !drawPile.Cards.Contains(card))
        {
            CardCmd.ClearAffliction(card);
            return false;
        }

        // Top insertion is LIFO; place the new card after the existing prepared queue.
        PreparedQueueCompatibility.Reposition(
            drawPile,
            card,
            Math.Min(preparedAhead, drawPile.Cards.Count));
        return true;
    }
}
