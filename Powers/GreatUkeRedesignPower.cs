using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class GreatUkeRedesignPower : RedesignV1CounterPower
{
    internal const int DamageThreshold = 10;
    private bool _consumed;
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named("GreatUkePower");

    public override decimal ModifyHpLostAfterOstyLate(
        Creature target,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource)
    {
        if (target != Owner || amount <= DamageThreshold || Amount <= 0)
        {
            return amount;
        }

        _consumed = true;
        return 0;
    }

    public override async Task AfterModifyingHpLostAfterOsty()
    {
        if (!_consumed)
        {
            return;
        }

        _consumed = false;
        Flash();
        await PowerCmd.Decrement(this);
    }
}
