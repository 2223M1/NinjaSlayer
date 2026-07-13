using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace NinjaSlayer.Powers;

public sealed class OpeningPower : NinjaSlayerPowerTemplate
{
    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override decimal ModifyDamageMultiplicative(
        Creature? target,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource,
        CardPlay? cardPlay)
    {
        if (Amount <= 0
            || target != Owner
            || !props.IsPoweredAttack()
            || dealer is null
            || Applier is null
            || dealer.Side != Applier.Side)
        {
            return 1m;
        }

        return 2m;
    }

    public override async Task AfterModifyingDamageAmount(CardModel? cardSource)
    {
        Flash();
        await PowerCmd.Decrement(this);
    }

    public override async Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants)
    {
        if (Applier is not null && side == Applier.Side)
        {
            await PowerCmd.Remove(this);
        }
    }
}
