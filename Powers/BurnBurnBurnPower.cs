using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class BurnBurnBurnPower : RedesignV1CounterPower
{
    public override PowerAssetProfile AssetProfile =>
        NinjaSlayerPowerAssets.Named("NarakuPower");
}
