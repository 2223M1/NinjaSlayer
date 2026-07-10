using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Interop.AutoRegistration;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class PursuitPower : NinjaSlayerPowerTemplate
{
    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;

    public int EnergyReward
    {
        get => DynamicVars.Energy.IntValue;
        set => DynamicVars.Energy.BaseValue = value;
    }

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.ForEnergy(this)
    ];

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new EnergyVar(2)
    ];

    public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer, DamageResult result, ValueProp props, Creature target, CardModel? cardSource)
    {
        if (target != Owner || dealer != Applier || !props.IsPoweredAttack() || result.TotalDamage <= 0)
        {
            return;
        }

        Flash();
        int remaining = await PowerCmd.ModifyAmount(choiceContext, this, -1, Applier, cardSource);
        if (remaining <= 0 && Applier?.Player != null)
        {
            await PlayerCmd.GainEnergy(EnergyReward, Applier.Player);
        }
    }
}
