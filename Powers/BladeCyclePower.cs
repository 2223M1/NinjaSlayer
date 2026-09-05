using MegaCrit.Sts2.Core.Entities.Powers;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class BladeCyclePower : RedesignV1CounterPower
{
    public override PowerStackType StackType => PowerStackType.Single;
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named("ExhaustForShurikenPower");
}
