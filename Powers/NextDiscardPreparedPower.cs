using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Afflictions;
using NinjaSlayer.Cards;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Code.Prepared;

namespace NinjaSlayer.Powers;

public sealed class NextDiscardPreparedPower : NinjaSlayerPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        if (cardSource is not null && cardSource.Affliction is null)
        {
            await CardCmd.Afflict<NextDiscardSourceAffliction>(cardSource, 1m);
        }
    }

    public override async Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel? clonedBy)
    {
        if (card.Owner != Owner.Player || oldPileType == PileType.Discard || card.Pile?.Type != PileType.Discard)
        {
            return;
        }

        bool hasSourceMarker = card.Affliction is NextDiscardSourceAffliction;
        NextDiscardProtectionDecision protection = NextDiscardProtectionPolicy.Resolve(
            Amount,
            hasSourceMarker,
            oldPileType == PileType.Play && card is NinjaApathy);
        if (hasSourceMarker)
        {
            CardCmd.ClearAffliction(card);
        }
        if (!protection.ShouldConsumeLayer)
        {
            return;
        }

        Flash();
        await PowerCmd.Decrement(this);
        await PrepareCmd.Apply(card);
    }

    public override async Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants)
    {
        if (participants.Contains(Owner))
        {
            await PowerCmd.Remove(this);
        }
    }

}
