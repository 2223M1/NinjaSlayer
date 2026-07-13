using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Code.Commands;

namespace NinjaSlayer.Powers;

public sealed class NextDiscardPreparedPower : NinjaSlayerPowerTemplate
{
    private readonly HashSet<CardModel> _protectedSourceCards = [];

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public override Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        if (cardSource is not null)
        {
            _protectedSourceCards.Add(cardSource);
        }

        return Task.CompletedTask;
    }

    public override async Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel? clonedBy)
    {
        if (card.Owner != Owner.Player || oldPileType == PileType.Discard || card.Pile?.Type != PileType.Discard)
        {
            return;
        }

        int unprotectedLayers = Amount - _protectedSourceCards.Count;
        bool isSourceCard = _protectedSourceCards.Remove(card);
        if (isSourceCard && unprotectedLayers <= 0)
        {
            return;
        }

        if (!isSourceCard && unprotectedLayers <= 0)
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
