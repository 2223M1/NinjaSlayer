using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

[RegisterPower]
public sealed class DamageFocusPower : ModPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.None;

    public Creature? FocusTarget { get; set; }
    public decimal DamageMultiplier { get; set; } = 1m;
    public decimal DefenseMultiplier { get; set; } = 1m;

    public override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (dealer == Owner && target == FocusTarget && props.HasFlag(ValueProp.Move))
        {
            return amount * DamageMultiplier;
        }

        return amount;
    }

    public override decimal ModifyHpLostAfterOsty(Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (target == Owner && dealer != FocusTarget && dealer?.IsEnemy == true)
        {
            return amount * DefenseMultiplier;
        }

        return amount;
    }

    public override async Task AfterSideTurnEnd(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext choiceContext, MegaCrit.Sts2.Core.Combat.CombatSide side, IEnumerable<Creature> participants)
    {
        if (Owner.Side == side)
        {
            await MegaCrit.Sts2.Core.Commands.PowerCmd.Remove(this);
        }
    }
}
