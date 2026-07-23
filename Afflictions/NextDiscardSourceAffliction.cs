using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Afflictions;

[RegisterAffliction]
public sealed class NextDiscardSourceAffliction : ModAfflictionTemplate
{
    public override Task AfterCardChangedPilesLate(
        CardModel card,
        PileType oldPileType,
        AbstractModel? clonedBy)
    {
        if (HasCard && ReferenceEquals(Card, card) && card.Pile?.Type != PileType.Play)
        {
            CardCmd.ClearAffliction(card);
        }

        return Task.CompletedTask;
    }
}
