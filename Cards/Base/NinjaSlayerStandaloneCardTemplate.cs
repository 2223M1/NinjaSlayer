using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

// Explicitly registered status, token, and ancient cards must not inherit the main card-pool registration.
public abstract class NinjaSlayerStandaloneCardTemplate : ModCardTemplate
{
    private readonly NinjaSlayerCardSpec _cardSpec;

    protected NinjaSlayerStandaloneCardTemplate(NinjaSlayerCardSpec cardSpec)
        : base(
            cardSpec.EnergyCost,
            cardSpec.Type,
            cardSpec.Rarity,
            cardSpec.TargetType,
            cardSpec.ShouldShowInCardLibrary)
    {
        _cardSpec = cardSpec;
    }

    public NinjaSlayerCardSpec Metadata => _cardSpec with { Tags = Tags.ToArray() };

    public override CardAssetProfile AssetProfile => _cardSpec.AssetName is { } assetName
        ? NinjaSlayerCardAssets.Named(assetName)
        : NinjaSlayerCardAssets.For(this);

    protected override HashSet<CardTag> CanonicalTags => _cardSpec.Tags?.ToHashSet() ?? [];
}
