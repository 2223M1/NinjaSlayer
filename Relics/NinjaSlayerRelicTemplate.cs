using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Relics;

[RegisterRelic(typeof(NinjaSlayerRelicPool), Inherit = true)]
public abstract class NinjaSlayerRelicTemplate : ModRelicTemplate
{
    public override RelicAssetProfile AssetProfile => NinjaSlayerRelicAssets.For(this);
}
