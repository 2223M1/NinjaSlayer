using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Code.Lifecycle;

namespace NinjaSlayer.Powers;

public sealed class NextDiscardPreparedPower : NinjaSlayerPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public override Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        if (cardSource is not null)
        {
            SourceProtectionState? state = CardPlayResolutionScope.GetOrCreateCardState(
                cardSource,
                this,
                static () => new SourceProtectionState());
            if (state is not null)
            {
                state.Protected = true;
            }
        }

        return Task.CompletedTask;
    }

    public override async Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel? clonedBy)
    {
        if (card.Owner != Owner.Player || oldPileType == PileType.Discard || card.Pile?.Type != PileType.Discard)
        {
            return;
        }

        bool isSourceCard = CardPlayResolutionScope.TryGetCardState(card, this, out SourceProtectionState? state)
            && state is { Protected: true };
        int unprotectedLayers = Amount - (isSourceCard ? 1 : 0);
        if (state is not null)
        {
            state.Protected = false;
        }
        if (unprotectedLayers <= 0)
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

    private sealed class SourceProtectionState
    {
        public bool Protected { get; set; }
    }
}
