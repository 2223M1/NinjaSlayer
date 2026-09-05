using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards.RedesignV1;

public abstract partial class NinjaSlayerRedesignCardTemplate(
    NinjaSlayerCardSpec cardSpec,
    string legacyArtName) : NinjaSlayerStandaloneCardTemplate(cardSpec)
{
    public override CardAssetProfile AssetProfile => NinjaSlayerCardAssets.Named(legacyArtName);

}
