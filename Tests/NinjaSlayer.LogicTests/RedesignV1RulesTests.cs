using NinjaSlayer.Content;

namespace NinjaSlayer.LogicTests;

public sealed class RedesignV1RulesTests
{
    [Fact]
    public void CharacterStartsAtSeventyTwoHpWithTenCards()
    {
        Assert.Equal(72, RedesignV1Rules.StartingHp);
        Assert.Equal(
            10,
            RedesignV1Rules.StartingStrikeCount
                + RedesignV1Rules.StartingDefendCount
                + RedesignV1Rules.StartingSignatureCardCount);
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
        Assert.Equal(74, all.Length);
        Assert.Equal(all.Length, all.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(RedesignV1Rules.ExcludedSpecialCardIds, all.Contains);
        Assert.Contains("TurtleShellRedesignV1", RedesignV1Rules.RareRewardCardIds);
        Assert.DoesNotContain("CountermeasureRedesignV1", RedesignV1Rules.ExcludedSpecialCardIds);
        Assert.Contains("StrongShurikenTokenRedesignV1", RedesignV1Rules.ExcludedSpecialCardIds);
        Assert.Contains("FinisherRedesignV1", RedesignV1Rules.ExcludedSpecialCardIds);
        Assert.Contains("BusyLine", RedesignV1Rules.ExcludedSpecialCardIds);
        Assert.DoesNotContain("PunchRedesignV1", RedesignV1Rules.ExcludedSpecialCardIds);
        Assert.Contains("SatsubatsuRedesignV1", RedesignV1Rules.CommonRewardCardIds);
        Assert.Contains("ChadoStillnessRedesignV1", RedesignV1Rules.CommonRewardCardIds);
        Assert.Contains("BladeReserveRedesignV1", RedesignV1Rules.CommonRewardCardIds);
        Assert.DoesNotContain("RoundhouseKickRedesignV1", RedesignV1Rules.CommonRewardCardIds);
        Assert.Contains("RoundhouseKickRedesignV1", RedesignV1Rules.UncommonRewardCardIds);
        Assert.Contains("EmptyShurikenRedesignV1", RedesignV1Rules.RareRewardCardIds);
        Assert.Contains("HellTornadoRedesignV1", RedesignV1Rules.RareRewardCardIds);
        Assert.Contains("TeaTeaRedesignV1", RedesignV1Rules.RareRewardCardIds);
        Assert.Contains("BurnBurnBurnRedesignV1", RedesignV1Rules.UncommonRewardCardIds);
        Assert.Contains("ReturnReturnReturnRedesignV1", RedesignV1Rules.UncommonRewardCardIds);
        Assert.DoesNotContain("BloodTearsRedesignV1", all);
        Assert.DoesNotContain("ChopChainRedesignV1", all);
        Assert.DoesNotContain("DoubleForceRedesignV1", all);
        Assert.DoesNotContain("EnduranceRedesignV1", all);
        Assert.DoesNotContain("ExecutionMoveRedesignV1", all);
        Assert.DoesNotContain("GauntletRedesignV1", all);
        Assert.DoesNotContain("KarateFormRedesignV1", all);
        Assert.DoesNotContain("ObserveBattleRedesignV1", all);
        Assert.DoesNotContain("ReadAndStrikeRedesignV1", all);
        Assert.Contains("LuckyStrikeRedesignV1", RedesignV1Rules.CommonRewardCardIds);
        Assert.Contains("KarateReversalRedesignV1", RedesignV1Rules.UncommonRewardCardIds);
        Assert.Contains("BladeCycleRedesignV1", RedesignV1Rules.UncommonRewardCardIds);

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
            Assert.DoesNotContain(archived, RedesignV1Rules.ExcludedSpecialCardIds);
        }

        Assert.Contains("StormFistRedesignV1", RedesignV1Rules.RareRewardCardIds);
        Assert.Contains("HiddenEdgeRedesignV1", RedesignV1Rules.UncommonRewardCardIds);
        Assert.Contains("AbandonThoughtRedesignV1", RedesignV1Rules.UncommonRewardCardIds);
        Assert.Contains("AlabamaDropRedesignV1", RedesignV1Rules.RareRewardCardIds);

        foreach (string added in new[]
                 {
                     "WhiskTeaFlashRedesignV1",
                     "OneDrinkOneStrikeRedesignV1",
                     "PreparedShurikenRedesignV1",
                     "ChopDefenseRedesignV1",
                     "RightHeavyPunchAfterSkillRedesignV1",
                     "FocusedMindRedesignV1",
                     "KarateTeaRedesignV1"
                 })
        {
            Assert.Contains(added, all);
        }

        Assert.Contains("StrongShurikenTokenRedesignV1", RedesignV1Rules.ExcludedSpecialCardIds);
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
            RedesignV1Rules.ResolveChadoBreathIncrease(amount, hasChadoInHand));
    }

    [Fact]
    public void ShurikenDamageIncludesEnhancementOnly()
    {
        Assert.Equal(7, RedesignV1Rules.ShurikenDamage(3));
        Assert.Equal(4, RedesignV1Rules.ShurikenDamage(-3));
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(0, 1, false)]
    [InlineData(3, 0, false)]
    public void OnlyAZeroSlotCharacterOwnsTheAutomaticallyCreatedShurikenSlot(
        int baseOrbSlots,
        int currentCapacity,
        bool expected)
    {
        Assert.Equal(
            expected,
            RedesignV1Rules.ShouldOwnTransientShurikenSlot(baseOrbSlots, currentCapacity));
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
        ShurikenStockResolution result = RedesignV1Rules.ResolveShurikenDiscard(
            stock,
            isOwnerDiscard,
            targetCount);

        Assert.Equal(expectedShots, result.Shots);
        Assert.Equal(expectedRemainingStock, result.RemainingStock);
    }

    [Theory]
    [InlineData(4, false, true, 1, 0, 4)]
    [InlineData(4, true, true, 1, 4, 3)]
    [InlineData(4, true, false, 1, 0, 4)]
    [InlineData(4, true, true, 0, 0, 4)]
    [InlineData(0, true, true, 1, 0, 0)]
    public void OnlyBladeCycleShuffleFiresAndConsumesOneStock(
        int stock,
        bool hasBladeCycle,
        bool isOwnerShuffle,
        int targetCount,
        int expectedShots,
        int expectedRemainingStock)
    {
        ShurikenStockResolution result = RedesignV1Rules.ResolveBladeCycleShuffle(
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
            RedesignV1Rules.IsBlackFlameTurnEndTarget(isAlive, isOwner, isSameSide));
        Assert.Equal(4, RedesignV1Rules.BlackFlameDamage);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 5)]
    [InlineData(-1, 0)]
    public void TurtleShellConvertsAllKarateToPlating(int karate, int expectedPlating)
    {
        Assert.Equal(
            expectedPlating,
            RedesignV1Rules.ResolveTurtleShellPlating(karate));
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
