using NinjaSlayer.Content;

namespace NinjaSlayer.LogicTests;

public sealed class CardPoolRulesTests
{
    [Fact]
    public void CharacterStartsAtSeventyTwoHpWithTenCards()
    {
        Assert.Equal(72, NinjaSlayerCardRules.StartingHp);
        Assert.Equal(
            10,
            NinjaSlayerCardRules.StartingStrikeCount
                + NinjaSlayerCardRules.StartingDefendCount
                + NinjaSlayerCardRules.StartingSignatureCardCount
                + NinjaSlayerCardRules.StartingPrejudgeCount);
    }

    [Fact]
    public void RewardCatalogHasTheLockedRarityCountsAndNoDuplicates()
    {
        Assert.Equal(NinjaSlayerCardRules.CommonRewardCount, NinjaSlayerCardRules.CommonRewardCardIds.Count);
        Assert.Equal(NinjaSlayerCardRules.UncommonRewardCount, NinjaSlayerCardRules.UncommonRewardCardIds.Count);
        Assert.Equal(NinjaSlayerCardRules.RareRewardCount, NinjaSlayerCardRules.RareRewardCardIds.Count);

        string[] all =
        [
            .. NinjaSlayerCardRules.CommonRewardCardIds,
            .. NinjaSlayerCardRules.UncommonRewardCardIds,
            .. NinjaSlayerCardRules.RareRewardCardIds
        ];
        Assert.Equal(80, all.Length);
        Assert.Equal(all.Length, all.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(NinjaSlayerCardRules.ExcludedSpecialCardIds, all.Contains);
        Assert.Contains("KarateBulwark", NinjaSlayerCardRules.RareRewardCardIds);
        Assert.DoesNotContain("CountermeasureRedesignV1", NinjaSlayerCardRules.ExcludedSpecialCardIds);
        Assert.Contains("StrongShuriken", NinjaSlayerCardRules.ExcludedSpecialCardIds);
        Assert.Contains("ReadAhead", NinjaSlayerCardRules.ExcludedSpecialCardIds);
        Assert.Contains("BusyLine", NinjaSlayerCardRules.ExcludedSpecialCardIds);
        Assert.DoesNotContain("PunchRedesignV1", NinjaSlayerCardRules.ExcludedSpecialCardIds);
        Assert.Contains("BS1260Kick", NinjaSlayerCardRules.CommonRewardCardIds);
        Assert.Contains("Stillness", NinjaSlayerCardRules.CommonRewardCardIds);
        Assert.Contains("BladePrep", NinjaSlayerCardRules.CommonRewardCardIds);
        Assert.DoesNotContain("DragonRoundhouseKick", NinjaSlayerCardRules.CommonRewardCardIds);
        Assert.Contains("DragonRoundhouseKick", NinjaSlayerCardRules.UncommonRewardCardIds);
        Assert.Contains("ShurikenStorm", NinjaSlayerCardRules.RareRewardCardIds);
        Assert.Contains("HellTornado", NinjaSlayerCardRules.RareRewardCardIds);
        Assert.Contains("Meditation", NinjaSlayerCardRules.RareRewardCardIds);
        Assert.Contains("BlackFlameInferno", NinjaSlayerCardRules.UncommonRewardCardIds);
        Assert.Contains("DevourFlame", NinjaSlayerCardRules.UncommonRewardCardIds);
        Assert.DoesNotContain("BloodTearsRedesignV1", all);
        Assert.DoesNotContain("ChopChainRedesignV1", all);
        Assert.DoesNotContain("DoubleForceRedesignV1", all);
        Assert.DoesNotContain("EnduranceRedesignV1", all);
        Assert.DoesNotContain("ExecutionMoveRedesignV1", all);
        Assert.DoesNotContain("GauntletRedesignV1", all);
        Assert.DoesNotContain("KarateFormRedesignV1", all);
        Assert.DoesNotContain("ObserveBattleRedesignV1", all);
        Assert.DoesNotContain("ReadAndStrikeRedesignV1", all);
        Assert.Contains("ReadTheEnemy", NinjaSlayerCardRules.CommonRewardCardIds);
        Assert.Contains("Endurance", NinjaSlayerCardRules.UncommonRewardCardIds);
        Assert.Contains("BladeCycle", NinjaSlayerCardRules.RareRewardCardIds);
        Assert.Contains("PressTheAttack", NinjaSlayerCardRules.UncommonRewardCardIds);
        Assert.DoesNotContain("NinjaGreetingRedesignV1", all);

        foreach (string archived in new[]
                 {
                     "CountermeasureRedesignV1",
                     "ReflexGuardRedesignV1",
                     "TrumpCardRedesignV1",
                     "ObserverGuardRedesignV1",
                     "OverexertRedesignV1",
                     "ChadoSecretRedesignV1",
                     "BloodTearsRedesignV1",
                     "ChopChainRedesignV1",
                     "DoubleForceRedesignV1",
                     "EnduranceRedesignV1",
                     "ExecutionMoveRedesignV1",
                     "GauntletRedesignV1",
                     "KarateFormRedesignV1",
                     "ObserveBattleRedesignV1",
                     "ReadAndStrikeRedesignV1"
                 })
        {
            Assert.DoesNotContain(archived, all);
            Assert.DoesNotContain(archived, NinjaSlayerCardRules.ExcludedSpecialCardIds);
        }

        Assert.Contains("StormFist", NinjaSlayerCardRules.RareRewardCardIds);
        Assert.Contains("HiddenEdge", NinjaSlayerCardRules.UncommonRewardCardIds);
        Assert.Contains("LetGo", NinjaSlayerCardRules.UncommonRewardCardIds);
        Assert.Contains("AlabamaDrop", NinjaSlayerCardRules.RareRewardCardIds);

        foreach (string added in new[]
                 {
                     "CatapultThrow",
                     "SomersaultKick",
                     "ReadyShuriken",
                     "CrossGuard",
                     "RightUppercut",
                     "Rekindle",
                     "Poise"
                 })
        {
            Assert.Contains(added, all);
        }

        Assert.Contains("StrongShuriken", NinjaSlayerCardRules.ExcludedSpecialCardIds);
    }

    [Theory]
    [InlineData(1, false, 0)]
    [InlineData(3, false, 2)]
    [InlineData(3, true, 3)]
    [InlineData(0, false, 0)]
    public void ChadoBreathSpendsOnePointOnlyWhenItMustRegenerateEnergy(
        int amount,
        bool hasChadoInHand,
        int expectedIncrease)
    {
        Assert.Equal(
            expectedIncrease,
            NinjaSlayerCardRules.ResolveChadoBreathIncrease(amount, hasChadoInHand));
    }

    [Theory]
    [InlineData(4, true, 1, 1, 3)]
    [InlineData(4, false, 1, 0, 4)]
    [InlineData(4, true, 0, 0, 4)]
    [InlineData(0, true, 1, 0, 0)]
    [InlineData(-1, true, 1, 0, 0)]
    public void DiscardFiresAndConsumesOneStock(
        int stock,
        bool isOwnerDiscard,
        int targetCount,
        int expectedShots,
        int expectedRemainingStock)
    {
        ShurikenStockResolution result = NinjaSlayerCardRules.ResolveShurikenDiscard(
            stock,
            isOwnerDiscard,
            targetCount);

        Assert.Equal(expectedShots, result.Shots);
        Assert.Equal(expectedRemainingStock, result.RemainingStock);
    }

    [Theory]
    [InlineData(4, false, true, 1, 0, 4)]
    [InlineData(4, true, true, 1, 4, 1)]
    [InlineData(4, true, false, 1, 0, 4)]
    [InlineData(4, true, true, 0, 0, 4)]
    [InlineData(0, true, true, 1, 0, 0)]
    public void OnlyBladeCycleShuffleFiresAndConsumesThreeStock(
        int stock,
        bool hasBladeCycle,
        bool isOwnerShuffle,
        int targetCount,
        int expectedShots,
        int expectedRemainingStock)
    {
        ShurikenStockResolution result = NinjaSlayerCardRules.ResolveBladeCycleShuffle(
            stock,
            hasBladeCycle,
            isOwnerShuffle,
            targetCount);

        Assert.Equal(expectedShots, result.Shots);
        Assert.Equal(expectedRemainingStock, result.RemainingStock);
    }

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, false, false, true)]
    [InlineData(true, false, true, false)]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, true, false)]
    public void BlackFlameTargetsOnlyItsLivingOwnerAndLivingEnemies(
        bool isAlive,
        bool isOwner,
        bool isSameSide,
        bool expected)
    {
        Assert.Equal(
            expected,
            NinjaSlayerCardRules.IsBlackFlameTurnEndTarget(isAlive, isOwner, isSameSide));
        Assert.Equal(4, NinjaSlayerCardRules.BlackFlameDamage);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 5)]
    [InlineData(-1, 0)]
    public void TurtleShellConvertsAllKarateToPlating(int karate, int expectedPlating)
    {
        Assert.Equal(
            expectedPlating,
            NinjaSlayerCardRules.ResolveTurtleShellPlating(karate));
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
        Assert.Equal(expectedWounds, NinjaSlayerCardRules.ResolveHardItOutWounds(damage, threshold));
    }
}
