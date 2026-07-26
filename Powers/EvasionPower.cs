using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class EvasionPower : NinjaSlayerPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public override decimal ModifyDamageCap(Creature? target, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
    {
        if (target != Owner || Amount <= 0)
        {
            return decimal.MaxValue;
        }

        if (AttackIntentPreviewContext.IsActive)
        {
            return decimal.MaxValue;
        }

        if (dealer is not { IsMonster: true } || !props.IsCardOrMonsterMove())
        {
            return decimal.MaxValue;
        }

        return 0m;
    }

    public override async Task AfterModifyingDamageAmount(CardModel? cardSource)
    {
        CombatDodgeAnimation.NotifyImpact(Owner);
        Flash();
        await PowerCmd.Decrement(this);
    }
}
