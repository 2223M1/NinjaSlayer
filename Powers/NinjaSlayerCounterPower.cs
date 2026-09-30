using MegaCrit.Sts2.Core.Entities.Powers;

namespace NinjaSlayer.Powers;

public abstract class NinjaSlayerCounterPower : NinjaSlayerPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
}
