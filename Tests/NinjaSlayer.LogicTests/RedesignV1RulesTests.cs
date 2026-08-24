using NinjaSlayer.Content;

namespace NinjaSlayer.LogicTests;

public sealed class RedesignV1RulesTests
{
    [Fact]
    public void RedesignIsEnabledByDefault()
    {
        Assert.True(new NinjaSlayerSettingsData().UseRedesignV1);
    }

    [Fact]
    public void NewRunRulesDefaultToRedesignV1()
    {
        Assert.Equal(NinjaSlayerRulesVersion.RedesignV1, new NinjaSlayerRunRules().RulesVersion);
    }

    [Fact]
    public void RedesignStartsAtSeventyTwoHp()
    {
        Assert.Equal(72, RedesignV1Rules.StartingHp);
    }

    [Fact]
    public void RewardCatalogHasTheLockedRarityCountsAndNoDuplicates()
    {
        Assert.Equal(RedesignV1Rules.CommonRewardCount, RedesignV1Rules.CommonRewardCardIds.Count);
        Assert.Equal(RedesignV1Rules.UncommonRewardCount, RedesignV1Rules.UncommonRewardCardIds.Count);
        Assert.Equal(RedesignV1Rules.RareRewardCount, RedesignV1Rules.RareRewardCardIds.Count);

        string[] all =
        [
            .. RedesignV1Rules.CommonRewardCardIds,
            .. RedesignV1Rules.UncommonRewardCardIds,
            .. RedesignV1Rules.RareRewardCardIds
        ];
        Assert.Equal(68, all.Length);
        Assert.Equal(all.Length, all.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(RedesignV1Rules.ExcludedSpecialCardIds, all.Contains);
        Assert.Contains("CountermeasureRedesignV1", RedesignV1Rules.CommonRewardCardIds);
        Assert.Contains("TurtleShellRedesignV1", RedesignV1Rules.ExcludedSpecialCardIds);
        Assert.Contains("BladeCycleRedesignV1", RedesignV1Rules.UncommonRewardCardIds);
    }

    [Theory]
    [InlineData(1, false, 0)]
    [InlineData(3, false, 2)]
    [InlineData(3, true, 3)]
    [InlineData(0, false, 0)]
    public void ChadoBreathSpendsOnePointOnlyWhenItMustRegenerateEnergy(
        int amount,
        bool hasChadoEnergy,
        int expectedIncrease)
    {
        Assert.Equal(
            expectedIncrease,
            RedesignV1Rules.ResolveChadoBreathIncrease(amount, hasChadoEnergy));
    }

    [Fact]
    public void ShurikenDamageIncludesStockBonus()
    {
        Assert.Equal(7, RedesignV1Rules.ShurikenDamage(3));
    }

    [Theory]
    [InlineData(4, false, true, 1, 4, 0)]
    [InlineData(4, true, true, 1, 4, 4)]
    [InlineData(4, false, false, 1, 0, 4)]
    [InlineData(4, false, true, 0, 0, 4)]
    [InlineData(0, true, true, 1, 0, 0)]
    [InlineData(-1, false, true, 1, 0, 0)]
    public void ShuffleFiresAllStockAndBladeCycleOnlyPreservesIt(
        int stock,
        bool preserveStock,
        bool isOwnerShuffle,
        int targetCount,
        int expectedShots,
        int expectedRemainingStock)
    {
        ShurikenShuffleResolution result = RedesignV1Rules.ResolveShurikenShuffle(
            stock,
            preserveStock,
            isOwnerShuffle,
            targetCount);

        Assert.Equal(expectedShots, result.Shots);
        Assert.Equal(expectedRemainingStock, result.RemainingStock);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 2, 2)]
    [InlineData(5, 0, 5)]
    [InlineData(5, 2, 7)]
    [InlineData(-1, 2, 2)]
    public void TurtleShellConvertsKarateAndAddsUpgradeBonus(
        int karate,
        int bonusPlating,
        int expectedPlating)
    {
        Assert.Equal(
            expectedPlating,
            RedesignV1Rules.ResolveTurtleShellPlating(karate, bonusPlating));
    }

    [Theory]
    [InlineData(0, 7, 0)]
    [InlineData(6, 7, 0)]
    [InlineData(7, 7, 1)]
    [InlineData(20, 7, 2)]
    [InlineData(20, 10, 2)]
    public void HardItOutConvertsAccumulatedUnblockedDamageIntoWounds(
        int damage,
        int threshold,
        int expectedWounds)
    {
        Assert.Equal(expectedWounds, RedesignV1Rules.ResolveHardItOutWounds(damage, threshold));
    }
}
