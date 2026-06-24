using MegaCrit.Sts2.Core.Entities.Relics;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Relics;

[RegisterRelic(typeof(NinjaSlayerRelicPool))]
public sealed class ReporterPassRelic : ModRelicTemplate
{
    public override RelicRarity Rarity => RelicRarity.Ancient;

    // ponytail: reuse the existing terminal relic art until Nancy gets dedicated icons.
    public override RelicAssetProfile AssetProfile => new(
        IconPath: "res://NinjaSlayer/images/relics/PortableIrcTerminalRelic.png",
        IconOutlinePath: "res://NinjaSlayer/images/relics/PortableIrcTerminalRelic_outline.png",
        BigIconPath: "res://NinjaSlayer/images/relics/PortableIrcTerminalRelic_large.png"
    );
}
