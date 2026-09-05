using MegaCrit.Sts2.Core.Commands;
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

    public override async Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target, DamageResult result, MegaCrit.Sts2.Core.ValueProps.ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (target != Owner || result.UnblockedDamage <= 0 || Amount <= 0)
        {
            return;
        }

        int absorbed = Math.Min(Amount, result.UnblockedDamage);
        await CreatureCmd.Heal(Owner, absorbed, playAnim: false);
        await PowerCmd.ModifyAmount(choiceContext, this, -absorbed, Owner, cardSource, silent: true);
    }
}
