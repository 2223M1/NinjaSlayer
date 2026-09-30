using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class BlackFlameInfernoPower : NinjaSlayerCounterPower
{
    public override PowerAssetProfile AssetProfile =>
        NinjaSlayerPowerAssets.Named(nameof(BlackFlameInfernoPower));
}
