using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Cards;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards.RedesignV1;

[RegisterCard(typeof(NinjaSlayerRedesignCardPool), Inherit = true)]
public abstract partial class NinjaSlayerRedesignCardTemplate(
    NinjaSlayerCardSpec cardSpec,
    string legacyArtName) : NinjaSlayerStandaloneCardTemplate(cardSpec)
{
    public override CardAssetProfile AssetProfile => NinjaSlayerCardAssets.Named(legacyArtName);

}
