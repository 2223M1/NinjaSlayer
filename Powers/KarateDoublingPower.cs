using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using STS2RitsuLib.Interop.AutoRegistration;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class KarateDoublingPower : NinjaSlayerPowerTemplate
{
    private class Data
    {
        public int AppliedTurnNumber = -1;
        public bool SkipRemovalOnce;
    }

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.None;

    public int AppliedTurnNumber
    {
        get => GetInternalData<Data>().AppliedTurnNumber;
        set => GetInternalData<Data>().AppliedTurnNumber = value;
    }

    protected override object InitInternalData() => new Data();

    public void ExtendToNextTurn()
    {
        GetInternalData<Data>().SkipRemovalOnce = true;
        Flash();
    }

    public override decimal ModifyPowerAmountGivenMultiplicative(PowerModel power, Creature giver, decimal amount, Creature? target, CardModel? cardSource)
    {
        if (giver == Owner && power is KaratePower && amount > 0)
        {
            return 2;
        }

        return 1;
    }

    public override Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        AppliedTurnNumber = Owner.Player?.PlayerCombatState?.TurnNumber ?? -1;
        return Task.CompletedTask;
    }

    public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        if (!participants.Contains(Owner))
        {
            return;
        }

        Data data = GetInternalData<Data>();
        if (data.SkipRemovalOnce)
        {
            data.SkipRemovalOnce = false;
            return;
        }

        await PowerCmd.Remove(this);
    }
}
