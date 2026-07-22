using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace NinjaSlayer.Content;

internal readonly record struct PotionFutureRewardBucket(CardRarity Rarity, CardType Type);

internal static class NinjaSlayerDebugRewardCoverage
{
    internal const int MinimumCandidatesPerBucket = 3;

    internal static readonly PotionFutureRewardBucket[] RequiredBuckets =
    [
        new(CardRarity.Common, CardType.Attack),
        new(CardRarity.Common, CardType.Skill),
        new(CardRarity.Uncommon, CardType.Attack),
        new(CardRarity.Uncommon, CardType.Skill),
        new(CardRarity.Uncommon, CardType.Power),
        new(CardRarity.Rare, CardType.Attack),
        new(CardRarity.Rare, CardType.Skill),
        new(CardRarity.Rare, CardType.Power)
    ];

    internal static void EnsurePotionFutureCoverage(IReadOnlyCollection<CardModel> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);

        Dictionary<PotionFutureRewardBucket, int> counts = cards
            .GroupBy(card => new PotionFutureRewardBucket(card.Rarity, card.Type))
            .ToDictionary(group => group.Key, group => group.Count());
        string[] gaps = RequiredBuckets
            .Select(bucket => (Bucket: bucket, Count: counts.GetValueOrDefault(bucket)))
            .Where(entry => entry.Count < MinimumCandidatesPerBucket)
            .Select(entry => $"{entry.Bucket.Rarity}/{entry.Bucket.Type}: {entry.Count}")
            .ToArray();

        if (gaps.Length > 0)
        {
            throw new InvalidOperationException(
                $"The NinjaSlayer debug card pool cannot satisfy The Future of Potions' " +
                $"{MinimumCandidatesPerBucket}-card rewards. Missing coverage: {string.Join(", ", gaps)}.");
        }
    }
}
