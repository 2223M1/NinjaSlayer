using MegaCrit.Sts2.Core.Entities.Relics;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Relics;

public sealed class ReporterPassRelic : NinjaSlayerRelicTemplate
{
    public override RelicRarity Rarity => RelicRarity.Ancient;

    // ponytail: reuse the existing terminal relic art until Nancy gets dedicated icons.
    public override RelicAssetProfile AssetProfile => NinjaSlayerRelicAssets.For<PortableIrcTerminalRelic>();
}
