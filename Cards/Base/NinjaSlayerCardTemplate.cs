using MegaCrit.Sts2.Core.Entities.Cards;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

[RegisterCard(typeof(NinjaSlayerCardPool), Inherit = true)]
public abstract class NinjaSlayerCardTemplate : ModCardTemplate
{
    protected NinjaSlayerCardTemplate(
        int energyCost,
        CardType type,
        CardRarity rarity,
        TargetType targetType,
        bool shouldShowInCardLibrary)
        : base(energyCost, type, rarity, targetType, shouldShowInCardLibrary)
    {
    }

    public override CardAssetProfile AssetProfile => NinjaSlayerCardAssets.For(this);
}
