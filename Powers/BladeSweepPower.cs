#if NINJASLAYER_CHANNEL_STABLE
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

// Stable predates native Aim. Keep the existing serialized ID for this card's buff.
public sealed class BladeSweepPower : NinjaSlayerCounterPower
{
    public override PowerAssetProfile AssetProfile =>
        NinjaSlayerPowerAssets.Named(nameof(BladeSweepPower));
}
#endif
