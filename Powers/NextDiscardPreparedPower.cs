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
        bool protectsSource = hasSourceMarker
            || oldPileType == PileType.Play && card is NinjaApathy;
        if (hasSourceMarker)
        {
            CardCmd.ClearAffliction(card);
        }
        if (Amount - (protectsSource ? 1 : 0) <= 0)
        {
            return;
        }

        if (!await PrepareCmd.Apply(card))
        {
            return;
        }

        Flash();
        await PowerCmd.Decrement(this);
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
