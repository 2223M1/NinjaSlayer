using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Content;

namespace NinjaSlayer.Powers;

public sealed class NarakuLifePower : NinjaSlayerPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public override Task AfterPowerAmountChanged(
        PlayerChoiceContext choiceContext,
        PowerModel power,
        decimal amount,
        Creature? applier,
        CardModel? cardSource)
    {
        if (ReferenceEquals(power, this) && Amount > 0)
        {
            CombatHealthBar.Refresh(Owner);
        }

        return Task.CompletedTask;
    }

    public override Task AfterRemoved(Creature oldOwner)
    {
        CombatHealthBar.Refresh(oldOwner);
        return Task.CompletedTask;
    }

    internal decimal AbsorbDamage(decimal amount)
    {
        int absorbed = Math.Min(Amount, Math.Max(0, (int)amount));
        if (absorbed == 0) return amount;

        Code.Telemetry.NinjaSlayerCombatTelemetry.Mechanic("naraku_absorbed", Owner, absorbed);

        // Commit before HP-loss hooks; previews do not enter this damage boundary.
        SetAmount(Amount - absorbed, silent: true);
        if (Amount == 0) RemoveInternal();
        CombatHealthBar.Refresh(Owner);
        return amount - absorbed;
    }
}
