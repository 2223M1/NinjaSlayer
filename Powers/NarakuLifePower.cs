using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Content;
using STS2RitsuLib.Combat.HealthBars;

namespace NinjaSlayer.Powers;

public sealed class NarakuLifePower : NinjaSlayerPowerTemplate, IHealthBarVisualGraftSource
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    protected override bool IsVisibleInternal => false;

    public HealthBarVisualGraftMetrics GetHealthBarVisualGraft(HealthBarVisualGraftContext context)
    {
        if (Amount <= 0 || context.Creature != Owner)
        {
            return new HealthBarVisualGraftMetrics(0);
        }

        return new HealthBarVisualGraftMetrics(
            Amount,
            NarakuLifeHealthBarColors.Foreground,
            null);
    }

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

    internal decimal AbsorbHpLoss(decimal amount)
    {
        int absorbed = Math.Min(Amount, Math.Max(0, (int)amount));
        if (absorbed == 0) return amount;

        Owner.GetPower<KillingIntentRedesignPower>()?.RecordDamage();

        // HP loss is synchronous. Commit the shield before the host evaluates death,
        // using the model operations that raise the native power/UI notifications.
        SetAmount(Amount - absorbed, silent: true);
        if (Amount == 0) RemoveInternal();
        CombatHealthBar.Refresh(Owner);
        return amount - absorbed;
    }
}
