using MegaCrit.Sts2.Core.Entities.Cards;

namespace NinjaSlayer.Cards;

public sealed record NinjaSlayerCardSpec(
    string Id,
    int EnergyCost,
    CardType Type,
    CardRarity Rarity,
    TargetType TargetType,
    bool ShouldShowInCardLibrary,
    string? AssetName = null,
    IReadOnlyCollection<CardTag>? Tags = null);
