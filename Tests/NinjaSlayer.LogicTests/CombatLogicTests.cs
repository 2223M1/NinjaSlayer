using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.LogicTests;

public sealed class CombatLogicTests
{
    [Theory]
    [InlineData(5, 0, 0)]
    [InlineData(5, 2, 9)]
    [InlineData(3, 10, 6)]
    public void KarateDamageUsesDescendingArithmetic(int stacks, int hits, int expected)
    {
        Assert.Equal(expected, KarateDamageMath.CumulativeDamage(stacks, hits));
    }

    [Fact]
    public void MemoSearchCachesWithoutConsumingTheBudget()
    {
        var memo = new BoundedMemoSearch<string, bool>(2, TimeSpan.FromSeconds(1));

        Assert.Equal(MemoSearchLookup.NewState, memo.Lookup("state-a", out _));
        memo.Store("state-a", true);
        Assert.Equal(MemoSearchLookup.Cached, memo.Lookup("state-a", out bool cached));
        Assert.True(cached);
        Assert.Equal(1, memo.VisitedStates);
        Assert.Equal(MemoSearchLookup.NewState, memo.Lookup("state-b", out _));
        Assert.Equal(MemoSearchLookup.StateBudgetExceeded, memo.Lookup("state-c", out _));
    }

    [Fact]
    public void MemoSearchHonorsAnExpiredTimeBudget()
    {
        var memo = new BoundedMemoSearch<string, bool>(2, TimeSpan.Zero);

        Assert.Equal(MemoSearchLookup.WatchdogExpired, memo.Lookup("state", out _));
        Assert.Equal(0, memo.VisitedStates);
    }

    [Fact]
    public void MemoSearchPrefersTheDeterministicStateBudgetOverTheWatchdog()
    {
        var memo = new BoundedMemoSearch<string, bool>(
            maximumStates: 0,
            maximumTime: TimeSpan.Zero,
            elapsed: () => TimeSpan.MaxValue);

        Assert.Equal(MemoSearchLookup.StateBudgetExceeded, memo.Lookup("state", out _));
    }

    [Fact]
    public void MemoSearchStillUsesTheWatchdogWhenStateBudgetRemains()
    {
        var memo = new BoundedMemoSearch<string, bool>(
            maximumStates: 1,
            maximumTime: TimeSpan.FromMilliseconds(1),
            elapsed: () => TimeSpan.FromMilliseconds(2));

        Assert.Equal(MemoSearchLookup.WatchdogExpired, memo.Lookup("state", out _));
    }

    [Fact]
    public void ForecastSearchKeysPreserveStructuredStateBoundaries()
    {
        var first = new FinisherForecastSearchKey<string>(
            FinisherForecastSearchStage.Hits, 1, 2, ["1|2", "3"]);
        var same = new FinisherForecastSearchKey<string>(
            FinisherForecastSearchStage.Hits, 1, 2, ["1|2", "3"]);
        var different = new FinisherForecastSearchKey<string>(
            FinisherForecastSearchStage.Hits, 1, 2, ["1", "2|3"]);

        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(first, different);
    }

    [Fact]
    public void FrameScopedCacheReusesOnlyTheCurrentFrameAndKey()
    {
        var cache = new FrameScopedCache<string, int>();
        cache.Store(10, "forecast-a", 42);

        Assert.True(cache.TryGet(10, "forecast-a", out int cached));
        Assert.Equal(42, cached);
        Assert.False(cache.TryGet(10, "forecast-b", out _));
        Assert.Equal(1, cache.Count);

        Assert.False(cache.TryGet(11, "forecast-a", out _));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void ScreenShakeSuppressionScopesRestoreNestedState()
    {
        Assert.False(ScreenShakeSuppressionContext.IsSuppressed);
        using (ScreenShakeSuppressionContext.Suppress())
        {
            Assert.True(ScreenShakeSuppressionContext.IsSuppressed);
            IDisposable inner = ScreenShakeSuppressionContext.Suppress();
            Assert.True(ScreenShakeSuppressionContext.IsSuppressed);
            inner.Dispose();
            inner.Dispose();
            Assert.True(ScreenShakeSuppressionContext.IsSuppressed);
        }

        Assert.False(ScreenShakeSuppressionContext.IsSuppressed);
    }

    [Fact]
    public void ScreenShakeSuppressionScopesTolerateOutOfOrderDisposal()
    {
        IDisposable outer = ScreenShakeSuppressionContext.Suppress();
        IDisposable inner = ScreenShakeSuppressionContext.Suppress();

        outer.Dispose();
        Assert.True(ScreenShakeSuppressionContext.IsSuppressed);

        inner.Dispose();
        Assert.False(ScreenShakeSuppressionContext.IsSuppressed);
    }

    [Fact]
    public void KarateCombatPreviewScopesRestoreNestedState()
    {
        var outerCard = new CardModel();
        var outerTarget = new Creature();
        var innerCard = new CardModel();
        var innerTarget = new Creature();

        using (KarateCombatPreviewContext.Enter(outerCard, outerTarget))
        {
            Assert.Same(outerCard, KarateCombatPreviewContext.CurrentCard);
            Assert.Same(outerTarget, KarateCombatPreviewContext.CurrentTarget);
            using (KarateCombatPreviewContext.Enter(innerCard, innerTarget))
            {
                Assert.Same(innerCard, KarateCombatPreviewContext.CurrentCard);
                Assert.Same(innerTarget, KarateCombatPreviewContext.CurrentTarget);
            }

            Assert.Same(outerCard, KarateCombatPreviewContext.CurrentCard);
            Assert.Same(outerTarget, KarateCombatPreviewContext.CurrentTarget);
        }

        Assert.Null(KarateCombatPreviewContext.CurrentCard);
        Assert.Null(KarateCombatPreviewContext.CurrentTarget);
    }

    [Fact]
    public void KarateCombatPreviewScopesDoNotRestoreDisposedAncestors()
    {
        var outerCard = new CardModel();
        var outerTarget = new Creature();
        var innerCard = new CardModel();
        var innerTarget = new Creature();
        IDisposable outer = KarateCombatPreviewContext.Enter(outerCard, outerTarget);
        IDisposable inner = KarateCombatPreviewContext.Enter(innerCard, innerTarget);

        outer.Dispose();
        Assert.Same(innerCard, KarateCombatPreviewContext.CurrentCard);
        Assert.Same(innerTarget, KarateCombatPreviewContext.CurrentTarget);

        inner.Dispose();
        Assert.Null(KarateCombatPreviewContext.CurrentCard);
        Assert.Null(KarateCombatPreviewContext.CurrentTarget);
    }

    [Fact]
    public void CombatMetricsResetOnlyTurnScopedValues()
    {
        object player = new();
        var metrics = new CombatMetricsSnapshot<object>(1, 0);
        metrics.AddGeneratedChado(player);
        metrics.MarkChadoDiscarded(player);
        metrics.MarkChadoExhausted(player);
        metrics.MarkHpLost(player);
        metrics.AddFinishedCard(player, isAttack: true, isMelee: true);

        Assert.Equal(1, metrics.GeneratedChado(player));
        Assert.True(metrics.ChadoDiscarded(player));
        Assert.True(metrics.ChadoExhausted(player));
        Assert.True(metrics.LostHp(player));
        Assert.True(metrics.PreviousFinishedWasAttack(player));
        Assert.Equal(1, metrics.MeleeAttacks(player));

        metrics.EnsureTurn(2, 0);

        Assert.Equal(1, metrics.GeneratedChado(player));
        Assert.False(metrics.ChadoDiscarded(player));
        Assert.False(metrics.ChadoExhausted(player));
        Assert.False(metrics.LostHp(player));
        Assert.True(metrics.PreviousFinishedWasAttack(player));
        Assert.Equal(0, metrics.MeleeAttacks(player));
    }

    [Fact]
    public void FinisherForecastHandlesDeterministicCombatEffects()
    {
        Assert.Equal(FinisherForecastOutcome.Guaranteed, EvaluateForecastForCorrectness(CreateForecast(
            [new ForecastTestState(5, 0, 0)], 1, FinisherForecastTargeting.Single, 5, singleTarget: 0)));
        Assert.Equal(FinisherForecastOutcome.NotGuaranteed, EvaluateForecastForCorrectness(CreateForecast(
            [new ForecastTestState(6, 0, 0)], 1, FinisherForecastTargeting.Single, 5, singleTarget: 0)));
        Assert.Equal(FinisherForecastOutcome.Guaranteed, EvaluateForecastForCorrectness(CreateForecast(
            [new ForecastTestState(5, 0, 0), new ForecastTestState(5, 0, 0)],
            1, FinisherForecastTargeting.All, 5)));
        Assert.Equal(FinisherForecastOutcome.NotGuaranteed, EvaluateForecastForCorrectness(CreateForecast(
            [new ForecastTestState(5, 0, 0), new ForecastTestState(6, 0, 0)],
            1, FinisherForecastTargeting.All, 5)));
        Assert.Equal(FinisherForecastOutcome.Guaranteed, EvaluateForecastForCorrectness(CreateForecast(
            [new ForecastTestState(5, 0, 0), new ForecastTestState(5, 0, 0)],
            2, FinisherForecastTargeting.Random, 5)));
        Assert.Equal(FinisherForecastOutcome.NotGuaranteed, EvaluateForecastForCorrectness(CreateForecast(
            [new ForecastTestState(5, 0, 0), new ForecastTestState(5, 0, 0)],
            1, FinisherForecastTargeting.Random, 5)));
        Assert.Equal(FinisherForecastOutcome.Guaranteed, EvaluateForecastForCorrectness(CreateForecast(
            [new ForecastTestState(5, 3, 0)], 1, FinisherForecastTargeting.Single, 8, singleTarget: 0)));
        Assert.Equal(FinisherForecastOutcome.Guaranteed, EvaluateForecastForCorrectness(CreateForecast(
            [new ForecastTestState(7, 0, 3)], 2, FinisherForecastTargeting.Single, 1,
            useKarate: true, singleTarget: 0)));
        Assert.Equal(FinisherForecastOutcome.Guaranteed, EvaluateForecastForCorrectness(CreateForecast(
            [new ForecastTestState(1, 0, 0), new ForecastTestState(2, 0, 0)],
            1, FinisherForecastTargeting.Random, 1, narakuSplash: 2)));
        Assert.Equal(FinisherForecastOutcome.NotGuaranteed, EvaluateForecastForCorrectness(CreateForecast(
            [new ForecastTestState(1, 0, 0)], 1, FinisherForecastTargeting.Single, 1,
            unknownEffect: true, singleTarget: 0)));
    }

    [Fact]
    public void FinisherForecastFailsClosedWhenItsBudgetIsExhausted()
    {
        FinisherForecastOutcome result = FinisherForecastEngine.Evaluate(
            CreateForecast(
                [new ForecastTestState(10, 0, 0), new ForecastTestState(10, 0, 0), new ForecastTestState(10, 0, 0)],
                20,
                FinisherForecastTargeting.Random,
                0),
            maximumSearchStates: 1,
            maximumSearchTime: TimeSpan.FromSeconds(1));

        Assert.Equal(FinisherForecastOutcome.IndeterminateBudget, result);
    }

    [Fact]
    public void FinisherForecastCanAcceptAnySuccessfulRandomBranch()
    {
        FinisherForecastSimulation<ForecastTestState, ForecastTestState> simulation =
            CreateRandomHitThenAllEffectForecast([new(5, 0, 0), new(10, 0, 0)], 5, 5);

        Assert.Equal(
            FinisherForecastOutcome.NotGuaranteed,
            FinisherForecastEngine.Evaluate(
                simulation,
                maximumSearchTime: TimeSpan.MaxValue,
                branchQuantifier: FinisherForecastBranchQuantifier.AllBranches));
        Assert.Equal(
            FinisherForecastOutcome.Guaranteed,
            FinisherForecastEngine.Evaluate(
                simulation,
                maximumSearchTime: TimeSpan.MaxValue,
                branchQuantifier: FinisherForecastBranchQuantifier.AnyBranch));
    }

    [Fact]
    public void FinisherForecastAnyBranchFailsWhenNoAssignmentClears()
    {
        FinisherForecastSimulation<ForecastTestState, ForecastTestState> simulation =
            CreateRandomHitThenAllEffectForecast([new(11, 0, 0), new(11, 0, 0)], 5, 5);

        Assert.Equal(
            FinisherForecastOutcome.NotGuaranteed,
            FinisherForecastEngine.Evaluate(
                simulation,
                maximumSearchTime: TimeSpan.MaxValue,
                branchQuantifier: FinisherForecastBranchQuantifier.AnyBranch));
    }

    [Fact]
    public void FinisherForecastAnyBranchPreservesBudgetIndeterminacy()
    {
        FinisherForecastOutcome result = FinisherForecastEngine.Evaluate(
            CreateForecast(
                [new ForecastTestState(10, 0, 0), new ForecastTestState(10, 0, 0)],
                20,
                FinisherForecastTargeting.Random,
                0),
            maximumSearchStates: 1,
            maximumSearchTime: TimeSpan.FromSeconds(1),
            branchQuantifier: FinisherForecastBranchQuantifier.AnyBranch);

        Assert.Equal(FinisherForecastOutcome.IndeterminateBudget, result);
    }

    [Fact]
    public void FinisherForecastAllowsPostEffectOnlySimulation()
    {
        FinisherForecastSimulation<ForecastTestState, ForecastTestState> simulation =
            CreateRandomHitThenAllEffectForecast([new(5, 0, 0)], 0, 5, hitCount: 0);

        Assert.Equal(
            FinisherForecastOutcome.Guaranteed,
            FinisherForecastEngine.Evaluate(
                simulation,
                maximumSearchTime: TimeSpan.MaxValue,
                branchQuantifier: FinisherForecastBranchQuantifier.AnyBranch));
    }

    [Fact]
    public void FinisherActionTrajectoriesKeepTheirAuthoredTimingAndEndpoints()
    {
        Assert.Equal(90f, FinisherActionTrajectory.FastTravelPixels);
        Assert.Equal(0.15f, FinisherActionTrajectory.FastTravelSeconds);
        Assert.Equal(0f, FinisherActionTrajectory.FastProgress(-1f));
        Assert.Equal(0.5f, FinisherActionTrajectory.FastProgress(0.5f), 5);
        Assert.Equal(1f, FinisherActionTrajectory.FastProgress(2f));

        Assert.Equal(120f, FinisherActionTrajectory.SlowTravelPixels);
        Assert.Equal(0.25f, FinisherActionTrajectory.SlowTravelSeconds);
        Assert.Equal(0f, FinisherActionTrajectory.SlowProgress(-1f));
        Assert.Equal(MathF.Pow(0.5f, 10f), FinisherActionTrajectory.SlowProgress(0.5f), 8);
        Assert.Equal(1f, FinisherActionTrajectory.SlowProgress(2f));
    }

    [Fact]
    public void NinjaSlayerFinisherTravelsContinuouslyFromItsOriginalPositionToImpact()
    {
        FinisherApproachPath fastRight = FinisherApproachPath.Create(
            FinisherApproachMode.ContinuousToImpact,
            actorX: 0f,
            targetX: 500f,
            targetHalfWidth: 50f,
            approachGap: 0f,
            authoredTravel: FinisherActionTrajectory.FastTravelPixels,
            fallbackDirection: 1f);
        Assert.Equal(0f, fastRight.TravelStartX);
        Assert.Equal(450f, fastRight.TravelEndX);
        Assert.Equal(450f, fastRight.ImpactX);
        Assert.True(
            fastRight.TravelEndX - fastRight.TravelStartX
            > FinisherActionTrajectory.FastTravelPixels);

        FinisherApproachPath slowLeft = FinisherApproachPath.Create(
            FinisherApproachMode.ContinuousToImpact,
            actorX: 500f,
            targetX: 0f,
            targetHalfWidth: 50f,
            approachGap: 0f,
            authoredTravel: FinisherActionTrajectory.SlowTravelPixels,
            fallbackDirection: -1f);
        Assert.Equal(500f, slowLeft.TravelStartX);
        Assert.Equal(50f, slowLeft.TravelEndX);
        Assert.Equal(50f, slowLeft.ImpactX);
    }

    [Theory]
    [InlineData(400f, 500f, 450f)]
    [InlineData(100f, 0f, 50f)]
    public void NinjaSlayerFinisherClampsNearTravelAtTheImpactPosition(
        float actorX,
        float targetX,
        float expectedImpactX)
    {
        FinisherApproachPath path = FinisherApproachPath.Create(
            FinisherApproachMode.ContinuousToImpact,
            actorX,
            targetX,
            targetHalfWidth: 50f,
            approachGap: 0f,
            authoredTravel: FinisherActionTrajectory.SlowTravelPixels,
            fallbackDirection: targetX >= actorX ? 1f : -1f);

        Assert.Equal(expectedImpactX, path.TravelEndX);
        Assert.Equal(expectedImpactX, path.ImpactX);
    }

    [Theory]
    [InlineData(20f, 430f)]
    [InlineData(430f, 20f)]
    public void InstantFinisherStartsAtImpactWithoutTravel(float actorX, float impactX)
    {
        FinisherApproachPath path = FinisherApproachPath.CreateToImpact(
            FinisherApproachMode.TeleportAtStart,
            actorX,
            impactX,
            authoredTravel: FinisherActionTrajectory.FastTravelPixels,
            fallbackDirection: impactX >= actorX ? 1f : -1f);

        Assert.Equal(actorX, path.OriginalX);
        Assert.Equal(impactX, path.TravelStartX);
        Assert.Equal(impactX, path.TravelEndX);
        Assert.Equal(impactX, path.ImpactX);
    }

    [Fact]
    public void FinisherWithoutContinuousMovementTeleportsOnlyAtItsPeak()
    {
        FinisherApproachPath path = FinisherApproachPath.CreateToImpact(
            FinisherApproachMode.TeleportAtPeak,
            actorX: 20f,
            impactX: 430f,
            authoredTravel: 0f,
            fallbackDirection: 1f);

        Assert.Equal(20f, path.TravelStartX);
        Assert.Equal(20f, path.TravelEndX);
        Assert.Equal(430f, path.ImpactX);
    }

    [Theory]
    [InlineData(0f, 500f, 330f, 450f)]
    [InlineData(500f, 0f, 170f, 50f)]
    public void YamotoKokiFinisherKeepsItsContinuousPreparationLunge(
        float actorX,
        float targetX,
        float expectedStartX,
        float expectedImpactX)
    {
        FinisherApproachPath path = FinisherApproachPath.Create(
            FinisherApproachMode.PrepositionThenLunge,
            actorX,
            targetX,
            targetHalfWidth: 50f,
            approachGap: 0f,
            authoredTravel: FinisherActionTrajectory.SlowTravelPixels,
            fallbackDirection: targetX >= actorX ? 1f : -1f);

        Assert.Equal(expectedStartX, path.TravelStartX);
        Assert.Equal(expectedImpactX, path.TravelEndX);
        Assert.Equal(expectedImpactX, path.ImpactX);
    }

    [Fact]
    public void DoomSquashKeepsHorizontalOriginAndAnchorsVerticalBottom()
    {
        Assert.Equal(
            FinisherSquashAnchorKind.Center,
            FinisherSquashAnchorPolicy.Resolve(scaleX: 0.55f, scaleY: 1.2f));
        Assert.Equal(
            FinisherSquashAnchorKind.BottomCenter,
            FinisherSquashAnchorPolicy.Resolve(scaleX: 1.2f, scaleY: 0.55f));
        Assert.Equal(
            FinisherSquashAnchorKind.Center,
            FinisherSquashAnchorPolicy.Resolve(scaleX: 1f, scaleY: 1f));
    }

    [Theory]
    [InlineData(-0.55f, 0.2f, 0.15f, 1.2f)]
    [InlineData(0.52f, 0.18f, -0.31f, 1.15f)]
    public void DoomSquashCompensationKeepsItsAnchorUnderMirroringAndTilt(
        float basisXx,
        float basisXy,
        float basisYx,
        float basisYy)
    {
        var anchorInParent = new FinisherAnchorPoint(320f, 460f);
        var anchorInBody = new FinisherAnchorPoint(-85f, 130f);
        var basisX = new FinisherAnchorPoint(basisXx, basisXy);
        var basisY = new FinisherAnchorPoint(basisYx, basisYy);

        FinisherAnchorPoint position = FinisherSquashAnchorPolicy.ResolveCompensatedPosition(
            anchorInParent,
            anchorInBody,
            basisX,
            basisY);
        float resolvedX = position.X
            + basisX.X * anchorInBody.X
            + basisY.X * anchorInBody.Y;
        float resolvedY = position.Y
            + basisX.Y * anchorInBody.X
            + basisY.Y * anchorInBody.Y;

        Assert.Equal(anchorInParent.X, resolvedX, 4);
        Assert.Equal(anchorInParent.Y, resolvedY, 4);
    }

    [Fact]
    public void BossDismembermentBuildsDeterministicGaplessVoronoiCells()
    {
        var bounds = new BossFragmentRect(-180f, -240f, 360f, 480f);

        IReadOnlyList<BossFragmentCell> first =
            BossDismembermentMath.BuildVoronoiCells(bounds, 7, 12345UL);
        IReadOnlyList<BossFragmentCell> second =
            BossDismembermentMath.BuildVoronoiCells(bounds, 7, 12345UL);

        Assert.Equal(7, first.Count);
        Assert.Equal(first.Select(cell => cell.Seed), second.Select(cell => cell.Seed));
        Assert.Equal(bounds.Width * bounds.Height, first.Sum(cell => cell.Area), 1);
        Assert.All(first, cell =>
        {
            Assert.InRange(cell.Centroid.X, bounds.X, bounds.X + bounds.Width);
            Assert.InRange(cell.Centroid.Y, bounds.Y, bounds.Y + bounds.Height);
        });
    }

    [Fact]
    public void BossSemanticOverlapUsesVisibleConvexHullsInsteadOfOnlyAabbs()
    {
        BossFragmentPoint[] upperLeftTriangle =
        [
            new(0f, 0f),
            new(100f, 0f),
            new(0f, 100f)
        ];
        BossFragmentPoint[] lowerRightTriangle =
        [
            new(100f, 100f),
            new(55f, 100f),
            new(100f, 55f)
        ];
        BossFragmentPoint[] overlappingTriangle =
        [
            new(45f, 45f),
            new(95f, 45f),
            new(45f, 95f)
        ];

        Assert.False(BossDismembermentMath.ConvexPolygonsOverlap(
            upperLeftTriangle,
            lowerRightTriangle));
        Assert.True(BossDismembermentMath.ConvexPolygonsOverlap(
            upperLeftTriangle,
            overlappingTriangle));
    }

    // Retired semantic-atlas tests are excluded while the simplified capture path is tested in game.
#if false
    [Fact]
    public void BossSemanticMergeKeepsAabbOnlyOverlapSeparateAndMergesVisibleInterleaving()
    {
        BossFragmentPoint[] upperLeftTriangle =
        [
            new(0f, 0f),
            new(100f, 0f),
            new(0f, 100f)
        ];
        BossFragmentPoint[] lowerRightTriangle =
        [
            new(100f, 100f),
            new(55f, 100f),
            new(100f, 55f)
        ];
        BossFragmentPoint[] overlappingTriangle =
        [
            new(45f, 45f),
            new(95f, 45f),
            new(45f, 95f)
        ];
        BossSemanticPartMergeInput first = CreateMergePart(
            sourceIndex: 0,
            boneId: 1UL,
            ancestors: [],
            hull: upperLeftTriangle,
            slotIndex: 10,
            minimumDrawOrder: 0,
            maximumDrawOrder: 2,
            visibleArea: 3_000f);
        BossSemanticPartMergeInput separate = CreateMergePart(
            sourceIndex: 1,
            boneId: 2UL,
            ancestors: [],
            hull: lowerRightTriangle,
            slotIndex: 11,
            minimumDrawOrder: 1,
            maximumDrawOrder: 3,
            visibleArea: 3_000f);
        BossSemanticPartMergeInput overlapping = separate with
        {
            Hull = overlappingTriangle,
            Bounds = BossDismembermentMath.BoundsOf(overlappingTriangle),
            AlphaSamples = overlappingTriangle
        };
        var fullBounds = new BossFragmentRect(0f, 0f, 100f, 100f);

        IReadOnlyList<BossSemanticPartMergeInput> separateResult =
            BossSemanticPartMergePolicy.Normalize(
                [first, separate],
                fullVisibleArea: 6_000f,
                fullBounds);
        IReadOnlyList<BossSemanticPartMergeInput> mergedResult =
            BossSemanticPartMergePolicy.Normalize(
                [first, overlapping],
                fullVisibleArea: 6_000f,
                fullBounds);

        Assert.Equal(2, separateResult.Count);
        BossSemanticPartMergeInput merged = Assert.Single(mergedResult);
        Assert.Equal([10, 11], merged.SlotIndices);
        Assert.Equal(0UL, merged.PrimaryBoneId);
        Assert.Equal([1UL, 2UL], merged.VisibleBoneIds);
    }

    [Fact]
    public void BossSemanticMergeFindsTheNearestContainingAncestorForTinyOverlays()
    {
        BossSemanticPartMergeInput root = CreateMergePart(
            sourceIndex: 0,
            boneId: 1UL,
            ancestors: [],
            hull: RectangleHull(0f, 0f, 100f, 100f),
            slotIndex: 10,
            minimumDrawOrder: 0,
            maximumDrawOrder: 0,
            visibleArea: 9_000f);
        BossSemanticPartMergeInput nearerButNonContaining = CreateMergePart(
            sourceIndex: 1,
            boneId: 2UL,
            ancestors: [1UL],
            hull: RectangleHull(120f, 20f, 20f, 20f),
            slotIndex: 11,
            minimumDrawOrder: 1,
            maximumDrawOrder: 1,
            visibleArea: 400f);
        BossSemanticPartMergeInput overlay = CreateMergePart(
            sourceIndex: 2,
            boneId: 3UL,
            ancestors: [2UL, 1UL],
            hull: RectangleHull(40f, 40f, 2f, 2f),
            slotIndex: 12,
            minimumDrawOrder: 2,
            maximumDrawOrder: 2,
            visibleArea: 4f);

        IReadOnlyList<BossSemanticPartMergeInput> result =
            BossSemanticPartMergePolicy.Normalize(
                [root, nearerButNonContaining, overlay],
                fullVisibleArea: 10_000f,
                new BossFragmentRect(0f, 0f, 140f, 100f));

        Assert.Equal(2, result.Count);
        BossSemanticPartMergeInput mergedRoot = Assert.Single(
            result,
            part => part.PrimaryBoneId == 1UL);
        Assert.Equal([10, 12], mergedRoot.SlotIndices);
        Assert.Single(result, part => part.PrimaryBoneId == 2UL);
    }

    [Fact]
    public void BossSemanticMergeNeverLeavesACompositeOnlyLayerAsItsOwnFragment()
    {
        BossSemanticPartMergeInput body = CreateMergePart(
            sourceIndex: 0,
            boneId: 1UL,
            ancestors: [],
            hull: RectangleHull(0f, 0f, 100f, 100f),
            slotIndex: 10,
            minimumDrawOrder: 0,
            maximumDrawOrder: 0,
            visibleArea: 8_000f);
        BossSemanticPartMergeInput glow = CreateMergePart(
            sourceIndex: 1,
            boneId: 2UL,
            ancestors: [1UL],
            hull: RectangleHull(10f, 10f, 80f, 80f),
            slotIndex: 11,
            minimumDrawOrder: 1,
            maximumDrawOrder: 1,
            visibleArea: 4_000f,
            hasNormalBlendSlot: false);

        BossSemanticPartMergeInput merged = Assert.Single(
            BossSemanticPartMergePolicy.Normalize(
                [body, glow],
                fullVisibleArea: 10_000f,
                new BossFragmentRect(0f, 0f, 100f, 100f)));

        Assert.True(merged.HasNormalBlendSlot);
        Assert.Equal(1UL, merged.PrimaryBoneId);
        Assert.Equal([10, 11], merged.SlotIndices);
    }
#endif

    [Fact]
    public void BossFragmentBoundsUseOneCenteredScaleAndRejectAspectDistortion()
    {
        var expected = new BossFragmentRect(-100f, -200f, 200f, 400f);
        Assert.True(BossDismembermentMath.TryResolveUniformBoundsCalibration(
            expected,
            new BossFragmentRect(-98f, -198f, 196f, 396f),
            out BossFragmentBoundsCalibration identity));
        Assert.True(identity.IsIdentity);

        Assert.True(BossDismembermentMath.TryResolveUniformBoundsCalibration(
            new BossFragmentRect(0f, 0f, 623.456f, 482.25f),
            new BossFragmentRect(0f, 0f, 607.5f, 463.862f),
            out BossFragmentBoundsCalibration lagavulin));
        Assert.InRange(lagavulin.UniformScale, 1.032f, 1.034f);
        Assert.InRange(lagavulin.CorrectedWidthRatio, 0.98f, 1.02f);
        Assert.InRange(lagavulin.CorrectedHeightRatio, 0.98f, 1.02f);

        Assert.True(BossDismembermentMath.TryResolveUniformBoundsCalibration(
            expected,
            new BossFragmentRect(-70f, -240f, 200f, 400f),
            out BossFragmentBoundsCalibration centeredTranslation));
        Assert.False(centeredTranslation.IsIdentity);
        Assert.Equal(1f, centeredTranslation.UniformScale, 4);
        Assert.Equal(-30f, centeredTranslation.TranslationX, 4);
        Assert.Equal(40f, centeredTranslation.TranslationY, 4);

        Assert.False(BossDismembermentMath.TryResolveUniformBoundsCalibration(
            expected,
            new BossFragmentRect(-200f, -400f, 400f, 800f),
            out _));
        Assert.False(BossDismembermentMath.TryResolveUniformBoundsCalibration(
            expected,
            new BossFragmentRect(-95f, -210f, 190f, 420f),
            out _));
        Assert.False(BossDismembermentMath.TryResolveUniformBoundsCalibration(
            expected,
            new BossFragmentRect(-94f, -200f, 188f, 400f),
            out _));
        Assert.False(BossDismembermentMath.TryResolveUniformBoundsCalibration(
            expected,
            new BossFragmentRect(float.NaN, -200f, 200f, 400f),
            out _));
        Assert.False(BossDismembermentMath.TryResolveUniformBoundsCalibration(
            expected,
            new BossFragmentRect(-100f, -200f, 0f, 400f),
            out _));
    }

#if false
    [Fact]
    public void BossSemanticPartsSplitOnlyWhenOversizedAndNeverPadToTwentyFour()
    {
        var fullBounds = new BossFragmentRect(0f, 0f, 1_000f, 800f);
        BossSemanticPartDefinition[] ordinary = Enumerable.Range(0, 5)
            .Select(index => CreateSemanticPart(
                index,
                x: index * 100f,
                y: 100f,
                width: 80f,
                height: 120f,
                areaRatio: 0.08f,
                boneId: (ulong)(index + 1)))
            .ToArray();
        Assert.Equal(
            ordinary.Length,
            BossSemanticPartPolicy.BuildFragments(ordinary, fullBounds, 1UL).Count);

        Assert.Equal(1, BossSemanticPartPolicy.ResolveSplitCount(0.219f, 0.44f, 0.44f, 4));
        Assert.Equal(2, BossSemanticPartPolicy.ResolveSplitCount(0.22f, 0.2f, 0.2f, 4));
        Assert.Equal(2, BossSemanticPartPolicy.ResolveSplitCount(0.1f, 0.45f, 0.2f, 4));

        BossSemanticPartDefinition[] crowded = Enumerable.Range(0, 21)
            .Select(index => CreateSemanticPart(
                index,
                x: index % 7 * 70f,
                y: index / 7 * 90f,
                width: 50f,
                height: 60f,
                areaRatio: 0.02f,
                boneId: (ulong)(index + 1)))
            .Append(CreateSemanticPart(
                21,
                x: 400f,
                y: 200f,
                width: 500f,
                height: 450f,
                areaRatio: 0.58f,
                boneId: 99UL))
            .ToArray();
        IReadOnlyList<BossSemanticFragmentDescriptor> fragments =
            BossSemanticPartPolicy.BuildFragments(crowded, fullBounds, 2UL);
        Assert.Equal(BossDismembermentMath.MaximumPieces, fragments.Count);
        Assert.Equal(3, fragments.Count(fragment => fragment.SemanticPartIndex == 21));
        Assert.All(
            crowded.Take(21),
            part => Assert.Single(
                fragments,
                fragment => fragment.SemanticPartIndex == part.PartIndex));
    }

    [Fact]
    public void BossSemanticPartsRejectOverCapacityInputInsteadOfSilentlyDroppingBones()
    {
        var fullBounds = new BossFragmentRect(0f, 0f, 1_000f, 800f);
        BossSemanticPartDefinition[] parts = Enumerable
            .Range(0, BossDismembermentMath.MaximumPieces + 1)
            .Select(index => CreateSemanticPart(
                index,
                x: index % 8 * 90f,
                y: index / 8 * 90f,
                width: 55f,
                height: 55f,
                areaRatio: 0.02f,
                boneId: (ulong)(index + 1)))
            .ToArray();

        Assert.Empty(BossSemanticPartPolicy.BuildFragments(parts, fullBounds, 3UL));
    }

    [Fact]
    public void BossOversizedSemanticPartsPlaceEverySplitSeedOnMeasuredAlpha()
    {
        BossFragmentPoint[] alphaSamples =
        [
            new(12f, 12f),
            new(12f, 88f),
            new(88f, 12f),
            new(88f, 88f),
            new(50f, 12f)
        ];
        BossSemanticPartDefinition part = CreateSemanticPart(
            0,
            0f,
            0f,
            100f,
            100f,
            areaRatio: 0.6f,
            boneId: 1UL) with
        {
            AlphaSamples = alphaSamples
        };

        IReadOnlyList<BossSemanticFragmentDescriptor> fragments =
            BossSemanticPartPolicy.BuildFragments(
                [part],
                new BossFragmentRect(0f, 0f, 100f, 100f),
                seed: 7UL);

        Assert.Equal(4, fragments.Count);
        Assert.All(fragments, fragment => Assert.Contains(fragment.Cell.Seed, alphaSamples));
        Assert.All(fragments, fragment =>
            Assert.All(fragment.LocalSeeds, seed => Assert.Contains(seed, alphaSamples)));
    }

    [Fact]
    public void BossSpineTopologyUsesStableBoneDataIndicesAndNearestFirstAncestors()
    {
        BossSpineBoneNode[] bones =
        [
            new(0, null),
            new(1, 0),
            new(2, 1),
            new(3, 0),
            new(4, 3)
        ];

        IReadOnlyDictionary<int, BossSpineBonePath> paths =
            BossSpineTopologyPolicy.BuildPaths(bones, detachedRootBoneIndex: 3);

        Assert.Equal(1UL, paths[0].BoneId);
        Assert.Equal(3UL, paths[2].BoneId);
        Assert.Equal([2UL, 1UL], paths[2].AncestorBoneIds);
        Assert.False(paths[2].BelongsToDetachedPart);
        Assert.True(paths[3].BelongsToDetachedPart);
        Assert.True(paths[4].BelongsToDetachedPart);
        Assert.Equal([4UL, 1UL], paths[4].AncestorBoneIds);
    }

    [Fact]
    public void BossSpineTopologyRejectsMissingParentsAndCycles()
    {
        Assert.Throws<InvalidOperationException>(() =>
            BossSpineTopologyPolicy.BuildPaths(
                [new BossSpineBoneNode(0, 9)],
                detachedRootBoneIndex: null));
        Assert.Throws<InvalidOperationException>(() =>
            BossSpineTopologyPolicy.BuildPaths(
                [new BossSpineBoneNode(0, 1), new BossSpineBoneNode(1, 0)],
                detachedRootBoneIndex: null));
    }

    [Fact]
    public void BossSpineDrawOrderUsesTheCurrentDeathPoseOrdering()
    {
        IReadOnlyDictionary<int, int> drawOrder =
            BossSpineTopologyPolicy.BuildDrawOrder(
                [0, 1, 2, 3],
                [2, 0, 3, 1]);

        Assert.Equal(0, drawOrder[2]);
        Assert.Equal(1, drawOrder[0]);
        Assert.Equal(2, drawOrder[3]);
        Assert.Equal(3, drawOrder[1]);
        Assert.Throws<InvalidOperationException>(() =>
            BossSpineTopologyPolicy.BuildDrawOrder([0, 1], [0, 0]));
    }

    [Fact]
    public void BossSemanticLinksUseOnlySmallOriginalBoneRelationsAndNeverReuseAPart()
    {
        BossSemanticPartCandidate[] parts =
        [
            new(0, 10UL, [], new(0f, 0f), new(0f, 0f), 0.07f, false, false),
            new(1, 11UL, [10UL], new(20f, 0f), new(10f, 0f), 0.06f, false, false),
            new(2, 12UL, [11UL, 10UL], new(40f, 0f), new(30f, 0f), 0.05f, false, false),
            new(3, 20UL, [], new(100f, 0f), new(100f, 0f), 0.04f, false, false),
            new(4, 21UL, [20UL], new(120f, 0f), new(110f, 0f), 0.04f, false, false),
            new(5, 30UL, [10UL], new(0f, 40f), new(0f, 20f), 0.03f, true, false),
            new(6, 31UL, [10UL], new(0f, 60f), new(0f, 30f), 0.03f, false, true)
        ];

        IReadOnlyList<BossSemanticBoneLink> links =
            BossSemanticPartPolicy.SelectOriginalBoneLinks(parts);

        Assert.Equal(2, links.Count);
        Assert.Contains(links, link => link.FirstPartIndex == 3 && link.SecondPartIndex == 4);
        Assert.Contains(links, link =>
            link.FirstPartIndex == 0 && link.SecondPartIndex == 1
            || link.FirstPartIndex == 1 && link.SecondPartIndex == 2);
        Assert.DoesNotContain(links, link =>
            link.FirstPartIndex == 0 && link.SecondPartIndex == 2);
        Assert.DoesNotContain(links, link => link.FirstPartIndex == 5 || link.SecondPartIndex == 5);
        Assert.DoesNotContain(links, link => link.FirstPartIndex == 6 || link.SecondPartIndex == 6);
        Assert.Equal(
            links.Count * 2,
            links.SelectMany(link => new[] { link.FirstPartIndex, link.SecondPartIndex })
                .Distinct()
                .Count());
    }

    [Fact]
    public void BossSemanticLinkConflictDoesNotReserveTheUnusedCandidateEndpoint()
    {
        BossSemanticPartCandidate[] parts =
        [
            new(0, 10UL, [], new(0f, 0f), new(0f, 0f), 0.04f, false, false),
            new(1, 11UL, [10UL], new(1f, 0f), new(1f, 0f), 0.04f, false, false),
            new(2, 12UL, [10UL], new(2f, 0f), new(2f, 0f), 0.04f, false, false),
            new(3, 13UL, [12UL, 10UL], new(5f, 0f), new(4f, 0f), 0.04f, false, false)
        ];

        IReadOnlyList<BossSemanticBoneLink> links =
            BossSemanticPartPolicy.SelectOriginalBoneLinks(parts);

        Assert.Equal(2, links.Count);
        Assert.Contains(links, link => link.FirstPartIndex == 0 && link.SecondPartIndex == 1);
        Assert.Contains(links, link => link.FirstPartIndex == 2 && link.SecondPartIndex == 3);
    }

    [Fact]
    public void BossSemanticLinksCrossOnlyInvisibleIntermediateBonesWithinOneDetachedDomain()
    {
        BossSemanticPartCandidate[] withoutVisibleIntermediate =
        [
            new(0, 1UL, [], new(0f, 0f), new(0f, 0f), 0.04f, true, false),
            new(1, 3UL, [2UL, 1UL], new(8f, 0f), new(4f, 0f), 0.04f, true, false)
        ];
        BossSemanticPartCandidate[] withVisibleIntermediate =
        [
            .. withoutVisibleIntermediate,
            new(2, 2UL, [1UL], new(4f, 0f), new(2f, 0f), 0.04f, true, false)
        ];
        BossSemanticPartCandidate[] acrossDetachedDomains =
        [
            withoutVisibleIntermediate[0],
            withoutVisibleIntermediate[1] with { BelongsToDetachedPart = false }
        ];

        BossSemanticBoneLink link = Assert.Single(
            BossSemanticPartPolicy.SelectOriginalBoneLinks(withoutVisibleIntermediate));
        Assert.Equal(2, link.BoneDistance);
        IReadOnlyList<BossSemanticBoneLink> visibleIntermediateLinks =
            BossSemanticPartPolicy.SelectOriginalBoneLinks(withVisibleIntermediate);
        Assert.DoesNotContain(visibleIntermediateLinks, candidate =>
            candidate.FirstPartIndex == 0 && candidate.SecondPartIndex == 1);
        Assert.Contains(visibleIntermediateLinks, candidate =>
            candidate.FirstPartIndex == 0 && candidate.SecondPartIndex == 2);
        Assert.Empty(BossSemanticPartPolicy.SelectOriginalBoneLinks(acrossDetachedDomains));
    }

    [Fact]
    public void BossSemanticLinksCannotCrossAnIntermediateBoneInsideAMergedPart()
    {
        BossSemanticPartCandidate[] parts =
        [
            new(0, 1UL, [], new(0f, 0f), new(0f, 0f), 0.04f, false, false),
            new(
                1,
                0UL,
                [],
                new(4f, 0f),
                new(2f, 0f),
                0.04f,
                false,
                false,
                [2UL, 20UL]),
            new(2, 3UL, [2UL, 1UL], new(8f, 0f), new(4f, 0f), 0.04f, false, false)
        ];

        IReadOnlyList<BossSemanticBoneLink> links =
            BossSemanticPartPolicy.SelectOriginalBoneLinks(parts);

        Assert.DoesNotContain(links, link =>
            link.FirstPartIndex == 0 && link.SecondPartIndex == 2);
    }

    [Fact]
    public void BossStagingVerticalFlipPreservesAbsoluteTileIdentity()
    {
        Assert.Equal(12, BossCaptureSamplingMath.MapViewportYToImage(256, 12, false));
        Assert.Equal(243, BossCaptureSamplingMath.MapViewportYToImage(256, 12, true));
        Assert.Equal(115, BossCaptureSamplingMath.MapViewportYToImage(256, 140, true));
        Assert.False(BossCaptureSamplingMath.ResolveReadbackVerticalFlip(1f, 0f));
        Assert.True(BossCaptureSamplingMath.ResolveReadbackVerticalFlip(0f, 1f));
        Assert.Throws<InvalidOperationException>(() =>
            BossCaptureSamplingMath.ResolveReadbackVerticalFlip(0f, 0f));
        Assert.Throws<InvalidOperationException>(() =>
            BossCaptureSamplingMath.ResolveReadbackVerticalFlip(0.8f, 0.7f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BossCaptureSamplingMath.MapViewportYToImage(256, 256, true));
    }

    [Fact]
    public void BossSpineBlendPolicyKeepsNormalLayersAndMergesCompositeOnlyLayers()
    {
        Assert.True(BossSemanticPartPolicy.IsNormalBlendMode(0L, null));
        Assert.False(BossSemanticPartPolicy.IsNormalBlendMode(1L, "normal"));
        Assert.False(BossSemanticPartPolicy.IsNormalBlendMode(null, "additive"));
        Assert.False(BossSemanticPartPolicy.IsNormalBlendMode(null, "Multiply"));
        Assert.False(BossSemanticPartPolicy.IsNormalBlendMode(null, "screen"));
        Assert.True(BossSemanticPartPolicy.IsNormalBlendMode(null, "unknown"));
    }
#endif

    [Fact]
    public void BossDismembermentBuildsDeterministicConnectedRagdollWithinThePieceCap()
    {
        BossFragmentPoint[] points = Enumerable.Range(0, 20)
            .Select(index => new BossFragmentPoint(
                index % 4 * 50f,
                index / 4 * 60f))
            .ToArray();

        IReadOnlyList<BossFragmentLink> first = BossDismembermentMath.BuildRagdollLinks(points);
        IReadOnlyList<BossFragmentLink> second = BossDismembermentMath.BuildRagdollLinks(points);

        Assert.InRange(first.Count, 1, BossDismembermentMath.MaximumPieces - 1);
        Assert.Equal(first, second);
        Assert.All(first, link =>
        {
            Assert.InRange(link.FirstIndex, 0, BossDismembermentMath.MaximumPieces - 1);
            Assert.InRange(link.SecondIndex, 0, BossDismembermentMath.MaximumPieces - 1);
        });
        List<int>[] adjacency = Enumerable.Range(0, BossDismembermentMath.MaximumPieces)
            .Select(_ => new List<int>())
            .ToArray();
        foreach (BossFragmentLink link in first)
        {
            adjacency[link.FirstIndex].Add(link.SecondIndex);
            adjacency[link.SecondIndex].Add(link.FirstIndex);
        }

        var visited = new HashSet<int>();
        var componentSizes = new List<int>();
        for (int start = 0; start < adjacency.Length; start++)
        {
            if (!visited.Add(start))
            {
                continue;
            }

            var queue = new Queue<int>();
            queue.Enqueue(start);
            int size = 0;
            while (queue.TryDequeue(out int current))
            {
                size++;
                foreach (int neighbor in adjacency[current])
                {
                    if (visited.Add(neighbor))
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }

            componentSizes.Add(size);
        }

        Assert.All(componentSizes, size => Assert.InRange(size, 1, 3));
        Assert.Contains(3, componentSizes);
    }

    [Fact]
    public void ArchitectSoftRagdollConnectsEveryFragmentUntilTheBurst()
    {
        BossFragmentPoint[] points = Enumerable.Range(0, 8)
            .Select(index => new BossFragmentPoint(index % 4 * 50f, index / 4 * 60f))
            .ToArray();

        IReadOnlyList<BossFragmentLink> links =
            BossDismembermentMath.BuildRagdollLinks(points, points.Length);

        Assert.Equal(points.Length - 1, links.Count);
        var visited = new HashSet<int> { 0 };
        while (true)
        {
            int previousCount = visited.Count;
            foreach (BossFragmentLink link in links)
            {
                if (visited.Contains(link.FirstIndex))
                {
                    visited.Add(link.SecondIndex);
                }
                if (visited.Contains(link.SecondIndex))
                {
                    visited.Add(link.FirstIndex);
                }
            }

            if (visited.Count == previousCount)
            {
                break;
            }
        }

        Assert.Equal(points.Length, visited.Count);
    }

    [Fact]
    public void BossDismembermentCollisionPaddingIsBounded()
    {
        Assert.Equal(18f, BossDismembermentMath.ResolveCollisionPadding(0f));
        Assert.InRange(BossDismembermentMath.ResolveCollisionPadding(10_000f), 18f, 42f);
        Assert.Equal(42f, BossDismembermentMath.ResolveCollisionPadding(1_000_000f));
    }

    [Fact]
    public void BossSoftBodyCollisionEnvelopeSeparatesVisibleHullFromJellyMargin()
    {
        SoftFragmentBody body = CreateSoftBody(0, default, 1f);
        body.SetCollisionEnvelope(hullScale: 1f, marginScale: 0f);
        (BossFragmentPoint hullMinimum, BossFragmentPoint hullMaximum) =
            body.ResolveCollisionAabb();
        Assert.Equal(-50f, hullMinimum.X, 3);
        Assert.Equal(50f, hullMaximum.X, 3);

        body.SetCollisionEnvelope(hullScale: 1f, marginScale: 1f);
        (BossFragmentPoint marginMinimum, BossFragmentPoint marginMaximum) =
            body.ResolveCollisionAabb();
        Assert.True(marginMinimum.X < hullMinimum.X);
        Assert.True(marginMaximum.X > hullMaximum.X);

        body.SetCollisionEnvelope(hullScale: 0.5f, marginScale: 0f);
        (BossFragmentPoint compressedMinimum, BossFragmentPoint compressedMaximum) =
            body.ResolveCollisionAabb();
        Assert.Equal(-25f, compressedMinimum.X, 3);
        Assert.Equal(25f, compressedMaximum.X, 3);
    }

    [Fact]
    public void BossFountainLaunchProfileUsesAStableUpwardMajority()
    {
        float[] masses = Enumerable.Range(0, BossDismembermentMath.MaximumPieces)
            .Select(index => 0.35f + index * 0.11f)
            .ToArray();
        IReadOnlyList<BossFountainLaunch> launches = BossFountainLaunchProfile.Create(
            masses,
            seed: 12345UL);

        Assert.Equal(12, launches.Count(launch => launch.Lane == BossFountainLaunchLane.Upward));
        Assert.Equal(2, launches.Count(launch => launch.Lane == BossFountainLaunchLane.Horizontal));
        Assert.Equal(2, launches.Count(launch => launch.Lane == BossFountainLaunchLane.Downward));
        Assert.All(
            launches.Where(launch => launch.Lane == BossFountainLaunchLane.Upward),
            launch => Assert.True(launch.Velocity.Y < 0f));
        Assert.All(
            launches.Where(launch => launch.Lane == BossFountainLaunchLane.Horizontal),
            launch => Assert.True(MathF.Abs(launch.Velocity.X) > MathF.Abs(launch.Velocity.Y)));
        Assert.All(
            launches.Where(launch => launch.Lane == BossFountainLaunchLane.Downward),
            launch => Assert.True(launch.Velocity.Y > 0f));
        Assert.All(launches, launch =>
        {
            float speed = MathF.Sqrt(
                launch.Velocity.X * launch.Velocity.X
                + launch.Velocity.Y * launch.Velocity.Y);
            Assert.InRange(
                speed,
                BossFountainLaunchProfile.MinimumLaunchSpeed - 0.01f,
                BossFountainLaunchProfile.MaximumLaunchSpeed + 0.01f);
            Assert.InRange(MathF.Abs(launch.AngularVelocityDegrees), 60f, 240f);
        });
        float totalMass = masses.Sum();
        float horizontalDrift = launches.Select((launch, index) =>
            launch.Velocity.X * masses[index]).Sum() / totalMass;
        Assert.InRange(
            MathF.Abs(horizontalDrift),
            0f,
            BossFountainLaunchProfile.MaximumHorizontalDrift + 0.01f);
        Assert.Equal(
            launches,
            BossFountainLaunchProfile.Create(masses, seed: 12345UL));
        Assert.NotEqual(
            launches,
            BossFountainLaunchProfile.Create(masses, seed: 54321UL));
    }

    [Fact]
    public void BossDismembermentMotionSeedIsReproducibleButChangesPerPresentation()
    {
        ulong fixedSeed = BossDismembermentMath.ResolveMotionSeed(11UL, 22UL, 33UL);

        Assert.Equal(fixedSeed, BossDismembermentMath.ResolveMotionSeed(11UL, 22UL, 33UL));
        Assert.NotEqual(fixedSeed, BossDismembermentMath.ResolveMotionSeed(11UL, 23UL, 33UL));
        Assert.NotEqual(fixedSeed, BossDismembermentMath.ResolveMotionSeed(11UL, 22UL, 34UL));
    }

    [Fact]
    public void BossDismembermentModelHashIsStableAcrossProcesses()
    {
        Assert.Equal(0xC0E6E60E6E945C74UL, BossDismembermentMath.StableHash64("ARCHITECT"));
        Assert.NotEqual(
            BossDismembermentMath.StableHash64("ARCHITECT"),
            BossDismembermentMath.StableHash64("THE_ARCHITECT"));
    }

    [Fact]
    public void BossBurstCompressionRetainsOnlyTwelvePercentOfTheRestLayout()
    {
        BossFragmentPoint packed = BossBurstCompressionLayout.ResolvePackedOrigin(
            new BossFragmentPoint(100f, 200f),
            restCenter: new BossFragmentPoint(300f, 100f),
            domainRestCenter: new BossFragmentPoint(200f, 200f));

        Assert.Equal(112f, packed.X, 4);
        Assert.Equal(188f, packed.Y, 4);
        Assert.Equal(0.12f, BossBurstCompressionLayout.RetainedRestOffset);
    }

    [Fact]
    public void BossFountainTrajectoryUsesBodyBurstGravityAndRisesBeforeFalling()
    {
        var sceneMaximum = new BossFragmentPoint(1_920f, 1_080f);
        var origin = new BossFragmentPoint(960f, 540f);
        float settlementFlightSeconds = BossBurstTimeline.VideoSeconds - 0.1f;
        IReadOnlyList<BossFountainLaunch> natural = BossFountainLaunchProfile.Create(
            Enumerable.Repeat(1f, BossDismembermentMath.MaximumPieces).ToArray(),
            seed: 98765UL);
        BossFountainLaunchPlan plan = BossFountainLaunchProfile.CreatePlan(natural);
        IReadOnlyList<BossFountainLaunch> launches = plan.Launches;
        var bodies = new List<SoftFragmentBody>(launches.Count);
        var actuators = new List<SoftBodyLaunchActuator>(launches.Count);
        var apexByFragment = new float?[launches.Count];
        var minimumYByFragment = Enumerable.Repeat(origin.Y, launches.Count).ToArray();
        for (int index = 0; index < launches.Count; index++)
        {
            SoftFragmentBody body = CreateSoftBody(index, origin, 0.7f);
            body.ConfigureDeformation(98765UL + (ulong)index);
            body.PinCompressed(
                origin,
                0.7f,
                phase: index * 0.37f,
                slideRadius: 0f,
                squashAmount: 0.22f);
            var actuator = new SoftBodyLaunchActuator(
                body,
                launches[index].Velocity,
                launches[index].AngularVelocityDegrees * MathF.PI / 180f);
            actuator.Begin();
            bodies.Add(body);
            actuators.Add(actuator);
        }

        var solver = new BossSoftBodySolver();
        const float step = 1f / 60f;
        int frameCount = (int)MathF.Ceiling(settlementFlightSeconds / step);
        for (int frame = 1; frame <= frameCount; frame++)
        {
            float elapsed = frame * step;
            float pump = SmoothStep(0f, 0.06f, elapsed);
            for (int index = 0; index < bodies.Count; index++)
            {
                bodies[index].TargetLinearScale = 0.7f + 0.3f * pump;
            }

            solver.Step(
                bodies,
                [],
                step,
                plan.Gravity,
                BossFountainLaunchProfile.LinearAirDrag,
                quadraticAirDrag: BossFountainLaunchProfile.QuadraticAirDrag,
                launchActuators: actuators,
                centerSpeedLimit: plan.MaximumCenterSpeed);
            for (int index = 0; index < bodies.Count; index++)
            {
                minimumYByFragment[index] = Math.Min(
                    minimumYByFragment[index],
                    bodies[index].Center.Y);
                if (apexByFragment[index] == null
                    && bodies[index].CenterVelocity.Y >= 0f
                    && launches[index].Lane == BossFountainLaunchLane.Upward)
                {
                    apexByFragment[index] = elapsed;
                }

            }
        }

        List<float> apexTimes = apexByFragment
            .Select((apex, index) => (apex, index))
            .Where(entry => entry.apex.HasValue
                && MathF.Abs(launches[entry.index].Velocity.X)
                    <= MathF.Abs(launches[entry.index].Velocity.Y))
            .Select(entry => entry.apex!.Value)
            .ToList();
        float medianApex = apexTimes.Order().ElementAt(apexTimes.Count / 2);
        int descending = bodies.Count(body => body.CenterVelocity.Y > 0f);
        int visiblyRising = launches
            .Select((launch, index) => (launch, index))
            .Count(entry => entry.launch.Lane == BossFountainLaunchLane.Upward
                && minimumYByFragment[entry.index] <= origin.Y - 5f);
        string diagnostics = $"gravity={plan.Gravity:F1}, median_apex={medianApex:F3}, "
            + $"rising={visiblyRising}, descending={descending}";
        Assert.Equal(1_650f, plan.Gravity);
        Assert.True(
            medianApex is >= 0.35f and <= 1f,
            diagnostics);
        Assert.True(visiblyRising >= 12, diagnostics);
        Assert.True(descending >= 14, diagnostics);
    }

    [Fact]
    public void BossSoftBodyUsesSixteenParticlesAndStartsAtTheRequestedCompression()
    {
        SoftFragmentBody body = CreateSoftBody(0, new BossFragmentPoint(10f, 20f), 0.72f);

        Assert.Equal(16, SoftFragmentBody.ParticleCount);
        Assert.Equal(10f, body.Center.X, 3);
        Assert.Equal(20f, body.Center.Y, 3);
        Assert.InRange(body.ResolveAreaRatio(), 0.51f, 0.53f);
        Assert.True(body.ResolveMinimumCellAreaRatio() > 0f);
    }

    [Fact]
    public void BossSoftBodyCompressionPinsEveryParticleAndReleasePumpsAreaOpen()
    {
        SoftFragmentBody body = CreateSoftBody(0, default, 0.7f);
        body.PinCompressed(default, 0.7f, phase: 0f, slideRadius: 0f);
        Assert.All(Enumerable.Range(0, SoftFragmentBody.ParticleCount), index =>
        {
            BossFragmentPoint point = body.GetParticlePosition(index);
            Assert.InRange(point.X, -35.01f, 35.01f);
            Assert.InRange(point.Y, -35.01f, 35.01f);
        });

        body.Release(default, 0f);
        body.TargetLinearScale = 1f;
        var solver = new BossSoftBodySolver();
        for (int step = 0; step < 4; step++)
        {
            solver.Step([body], [], 1f / 60f, gravity: 0f, airDrag: 0.2f);
        }

        BossFragmentPoint[] pumpedPoints = Enumerable.Range(0, SoftFragmentBody.ParticleCount)
            .Select(body.GetParticlePosition)
            .ToArray();
        Assert.True(
            body.ResolveAreaRatio() > 0.8f,
            $"area={body.ResolveAreaRatio():F3}, min={body.ResolveMinimumCellAreaRatio():F3}, "
                + $"rollbacks={body.RollbackCount}, inversions={body.InversionCount}, "
                + $"rejected_area={body.LastRejectedAreaRatio:F3}, "
                + $"rejected_speed={body.LastRejectedParticleSpeed:F3}, "
                + $"width={pumpedPoints.Max(point => point.X) - pumpedPoints.Min(point => point.X):F3}, "
                + $"height={pumpedPoints.Max(point => point.Y) - pumpedPoints.Min(point => point.Y):F3}");
        Assert.True(body.ResolveMinimumCellAreaRatio() > 0f);
    }

    [Fact]
    public void BossSoftBodyGravityIsDownwardAndAirDragDissipatesHorizontalSpeed()
    {
        SoftFragmentBody body = CreateSoftBody(0, default, 1f);
        body.Release(new BossFragmentPoint(100f, -20f), 0f);
        body.Predict(0.1f, gravity: 200f, airDrag: 2f);

        BossFragmentPoint velocity = body.CenterVelocity;
        Assert.InRange(velocity.X, 81f, 82f);
        Assert.InRange(velocity.Y, 3f, 4f);
    }

    [Fact]
    public void BossSoftBodyRestStateIsStableAndDoesNotInvert()
    {
        SoftFragmentBody body = CreateSoftBody(0, default, 1f);
        body.Release(default, 0f);
        var solver = new BossSoftBodySolver();
        for (int step = 0; step < 120; step++)
        {
            solver.Step([body], [], 1f / 60f, gravity: 0f, airDrag: 0.4f);
        }

        Assert.InRange(body.ResolveAreaRatio(), 0.999f, 1.001f);
        Assert.InRange(body.ResolveMaximumStretch(), 0.999f, 1.001f);
        Assert.True(body.ResolveMinimumCellAreaRatio() > 0.999f);
    }

    [Fact]
    public void BossSoftBodyContactCorrectionIsLocalToTheContactGridCell()
    {
        SoftFragmentBody body = CreateSoftBody(0, default, 1f);
        BossFragmentPoint nearBefore = body.GetParticlePosition(0);
        BossFragmentPoint farBefore = body.GetParticlePosition(15);

        float inverseMass = body.GetEffectiveInverseMass(0f, 0f);
        body.ApplyContactPositionImpulse(
            0f,
            0f,
            new BossFragmentPoint(1f, 0f),
            12f / inverseMass);

        Assert.True(body.GetParticlePosition(0).X - nearBefore.X > 11f);
        Assert.Equal(farBefore, body.GetParticlePosition(15));
    }

    [Fact]
    public void BossSoftBodyInitialOverlapArmsAfterSeparationAndThenCollides()
    {
        SoftFragmentBody first = CreateSoftBody(1, new BossFragmentPoint(-12f, 0f), 1f);
        SoftFragmentBody second = CreateSoftBody(2, new BossFragmentPoint(12f, 0f), 1f);
        first.SetCollisionEnvelope(hullScale: 1f, marginScale: 1f);
        second.SetCollisionEnvelope(hullScale: 1f, marginScale: 1f);
        var broadphase = new SoftCollisionBroadphase(20f);
        Assert.Single(broadphase.BuildPairs([first, second]));

        first.Release(default, 0f);
        second.Release(default, 0f);
        var solver = new BossSoftBodySolver();
        float openingDistance = MathF.Abs(second.Center.X - first.Center.X);
        int openingContacts = 0;
        for (int step = 0; step < 8; step++)
        {
            openingContacts += solver.Step(
                [first, second],
                [],
                1f / 60f,
                gravity: 0f,
                airDrag: 0.6f).Contacts;
        }

        Assert.Equal(0, openingContacts);
        Assert.InRange(
            MathF.Abs(second.Center.X - first.Center.X),
            openingDistance - 0.01f,
            openingDistance + 0.01f);

        for (int index = 0; index < SoftFragmentBody.ParticleCount; index++)
        {
            second.ApplyParticleCorrection(index, new BossFragmentPoint(180f, 0f));
        }

        _ = solver.Step([first, second], [], 1f / 60f, gravity: 0f, airDrag: 0f);
        first.Release(new BossFragmentPoint(240f, 0f), 0f);
        second.Release(new BossFragmentPoint(-240f, 0f), 0f);
        int contactStarts = 0;
        for (int step = 0; step < 40 && contactStarts == 0; step++)
        {
            contactStarts += solver.Step(
                [first, second],
                [],
                1f / 60f,
                gravity: 0f,
                airDrag: 0f).ContactStarts;
        }

        Assert.True(contactStarts > 0);
        float contactDistance = MathF.Abs(second.Center.X - first.Center.X);
        for (int step = 0; step < 60; step++)
        {
            _ = solver.Step(
                [first, second],
                [],
                1f / 60f,
                gravity: 0f,
                airDrag: 0.1f);
        }

        float finalDistance = MathF.Abs(second.Center.X - first.Center.X);
        Assert.True(
            finalDistance > contactDistance,
            $"contact={contactDistance:F3}, final={finalDistance:F3}, "
                + $"first_vx={first.CenterVelocity.X:F3}, second_vx={second.CenterVelocity.X:F3}");
        Assert.True(
            first.Center.X < second.Center.X,
            $"Bodies tunneled through each other: first={first.Center.X:F3}, "
                + $"second={second.Center.X:F3}.");
        Assert.True(first.ResolveMinimumCellAreaRatio() > 0f);
        Assert.True(second.ResolveMinimumCellAreaRatio() > 0f);
    }

    [Fact]
    public void ArchitectSoftRagdollUsesParticleFloorContactsAndUnbreakableLeadLinks()
    {
        SoftFragmentBody first = CreateSoftBody(1, new BossFragmentPoint(-45f, -90f), 1f);
        SoftFragmentBody second = CreateSoftBody(2, new BossFragmentPoint(45f, -90f), 1f);
        first.SetCollisionEnvelope(hullScale: 1f, marginScale: 0f);
        second.SetCollisionEnvelope(hullScale: 1f, marginScale: 0f);
        first.Release(new BossFragmentPoint(90f, -140f), 0.2f);
        second.Release(new BossFragmentPoint(100f, -125f), -0.15f);
        var link = new SoftRagdollLink(first, 7, second, 4, restLength: 90f)
        {
            CanBreak = false
        };
        var solver = new BossSoftBodySolver();
        for (int step = 0; step < 90; step++)
        {
            solver.Step(
                [first, second],
                [link],
                1f / 60f,
                gravity: 860f,
                airDrag: 0.08f,
                floorY: 0f);
        }

        Assert.False(link.Broken);
        Assert.All(Enumerable.Range(0, SoftFragmentBody.ParticleCount), index =>
        {
            Assert.True(first.GetParticlePosition(index).Y <= 0.001f);
            Assert.True(second.GetParticlePosition(index).Y <= 0.001f);
        });
        Assert.True(first.ResolveMinimumCellAreaRatio() > 0f);
        Assert.True(second.ResolveMinimumCellAreaRatio() > 0f);
    }

    [Fact]
    public void BossSoftBodyPreservesMirroredRestOrientation()
    {
        SoftFragmentBody body = CreateMirroredSoftBody();
        body.Release(default, 0f);
        var solver = new BossSoftBodySolver();
        for (int step = 0; step < 120; step++)
        {
            solver.Step([body], [], 1f / 60f, gravity: 0f, airDrag: 0.4f);
        }

        Assert.InRange(body.ResolveAreaRatio(), 0.999f, 1.001f);
        Assert.True(body.ResolveMinimumCellAreaRatio() > 0.999f);
    }

    [Fact]
    public void BossSoftBodyIsApproximatelyInvariantAcrossSixtyAndOneTwentyHertz()
    {
        SoftFragmentBody atSixty = CreateSoftBody(1, default, 0.7f);
        SoftFragmentBody atOneTwenty = CreateSoftBody(2, default, 0.7f);
        atSixty.Release(new BossFragmentPoint(180f, -120f), 1.2f);
        atOneTwenty.Release(new BossFragmentPoint(180f, -120f), 1.2f);
        atSixty.TargetLinearScale = 1f;
        atOneTwenty.TargetLinearScale = 1f;
        var sixtySolver = new BossSoftBodySolver();
        var oneTwentySolver = new BossSoftBodySolver();

        for (int step = 0; step < 60; step++)
        {
            sixtySolver.Step([atSixty], [], 1f / 60f, gravity: 320f, airDrag: 0.3f);
        }

        for (int step = 0; step < 120; step++)
        {
            oneTwentySolver.Step([atOneTwenty], [], 1f / 120f, gravity: 320f, airDrag: 0.3f);
        }

        Assert.InRange(MathF.Abs(atSixty.Center.X - atOneTwenty.Center.X), 0f, 2f);
        Assert.InRange(MathF.Abs(atSixty.Center.Y - atOneTwenty.Center.Y), 0f, 3f);
        Assert.InRange(
            MathF.Abs(atSixty.ResolveAreaRatio() - atOneTwenty.ResolveAreaRatio()),
            0f,
            0.03f);
    }

    [Fact]
    public void BossSoftBodyBroadphaseReusesOnlyTheCurrentFrameCells()
    {
        SoftFragmentBody body = CreateSoftBody(1, default, 1f);
        body.SetCollisionEnvelope(hullScale: 1f, marginScale: 1f);
        var broadphase = new SoftCollisionBroadphase(32f);
        broadphase.BuildPairs([body]);
        int initialCells = broadphase.ActiveCellCount;

        body.Release(new BossFragmentPoint(10_000f, 0f), 0f);
        body.Predict(0.1f, gravity: 0f, airDrag: 0f);
        broadphase.BuildPairs([body]);
        var freshBroadphase = new SoftCollisionBroadphase(32f);
        freshBroadphase.BuildPairs([body]);

        Assert.True(initialCells > 0);
        Assert.Equal(freshBroadphase.ActiveCellCount, broadphase.ActiveCellCount);
    }

    [Fact]
    public void BossSoftBodyBroadphaseRejectsNonFiniteAndRunawayBounds()
    {
        SoftFragmentBody nonFinite = CreateSoftBody(1, default, 1f);
        nonFinite.SetCollisionEnvelope(hullScale: 1f, marginScale: 1f);
        nonFinite.ApplyParticleCorrection(0, new BossFragmentPoint(float.NaN, 0f));
        var broadphase = new SoftCollisionBroadphase(32f);

        Assert.Empty(broadphase.BuildPairs([nonFinite]));
        Assert.Equal(0, broadphase.ActiveCellCount);

        SoftFragmentBody runaway = CreateSoftBody(2, default, 1f);
        runaway.SetCollisionEnvelope(hullScale: 1f, marginScale: 1f);
        runaway.ApplyParticleCorrection(0, new BossFragmentPoint(1_000_000f, 0f));

        Assert.Empty(broadphase.BuildPairs([runaway]));
        Assert.Equal(0, broadphase.ActiveCellCount);
    }

    [Fact]
    public void BossSoftBodyInvalidFragmentCannotContaminateItsRagdollNeighbor()
    {
        SoftFragmentBody invalid = CreateSoftBody(1, default, 1f);
        SoftFragmentBody healthy = CreateSoftBody(2, new BossFragmentPoint(120f, 0f), 1f);
        invalid.ApplyParticleCorrection(0, new BossFragmentPoint(float.NaN, 0f));
        var link = new SoftRagdollLink(invalid, 3, healthy, 0, restLength: 20f)
        {
            CanBreak = false
        };

        new BossSoftBodySolver().Step(
            [invalid, healthy],
            [link],
            1f / 60f,
            gravity: 860f,
            airDrag: 0.08f);

        Assert.False(invalid.HasFiniteState);
        Assert.True(healthy.HasFiniteState);
        Assert.True(link.Broken);
        Assert.All(Enumerable.Range(0, SoftFragmentBody.ParticleCount), index =>
        {
            BossFragmentPoint point = healthy.GetParticlePosition(index);
            Assert.True(float.IsFinite(point.X));
            Assert.True(float.IsFinite(point.Y));
        });
    }

    [Fact]
    public void BossSoftBodyCollisionResponseDoesNotAmplifyProjectionEnergy()
    {
        SoftFragmentBody first = CreateSoftBody(1, new BossFragmentPoint(-55f, 0f), 1f);
        SoftFragmentBody second = CreateSoftBody(2, new BossFragmentPoint(55f, 0f), 1f);
        first.SetCollisionEnvelope(hullScale: 1f, marginScale: 1f);
        second.SetCollisionEnvelope(hullScale: 1f, marginScale: 1f);
        first.Release(new BossFragmentPoint(180f, 0f), 0f);
        second.Release(new BossFragmentPoint(-180f, 0f), 0f);

        var solver = new BossSoftBodySolver();
        int manifolds = 0;
        int maximumManifoldsPerStep = 0;
        float energyBefore = 0f;
        float energyAfter = 0f;
        float maximumCenterSpeed = 0f;
        float relativeHorizontalSpeed = float.NegativeInfinity;
        for (int frame = 0; frame < 60; frame++)
        {
            SoftBodyStepMetrics metrics = solver.Step(
                [first, second],
                [],
                1f / 60f,
                gravity: 0f,
                airDrag: 0f);
            manifolds += metrics.Contacts;
            maximumManifoldsPerStep = Math.Max(maximumManifoldsPerStep, metrics.Contacts);
            energyBefore += metrics.ContactEnergyBefore;
            energyAfter += metrics.ContactEnergyAfter;
            maximumCenterSpeed = Math.Max(maximumCenterSpeed, metrics.MaximumCenterSpeed);
            relativeHorizontalSpeed = second.CenterVelocity.X - first.CenterVelocity.X;
            if (relativeHorizontalSpeed >= 0f)
            {
                break;
            }
        }

        Assert.True(
            relativeHorizontalSpeed is >= 1f and <= 360f,
            $"relative={relativeHorizontalSpeed:F3}, manifolds={manifolds}, "
                + $"energy_before={energyBefore:F3}, energy_after={energyAfter:F3}");
        Assert.True(energyAfter <= energyBefore * 1.011f + 1f);
        Assert.InRange(maximumManifoldsPerStep, 1, BossSoftBodySolver.MaximumSubsteps);
        Assert.InRange(
            maximumCenterSpeed,
            0f,
            BossSoftBodySolver.DefaultMaximumCenterSpeed);
        Assert.True(first.HasFiniteState);
        Assert.True(second.HasFiniteState);
    }

    [Fact]
    public void BossSoftBodyRagdollLinkAllowsAdjacentFragmentsWithoutAnArtificialGap()
    {
        SoftFragmentBody first = CreateSoftBody(1, default, 1f);
        SoftFragmentBody second = CreateSoftBody(2, default, 1f);
        var link = new SoftRagdollLink(first, 0, second, 0, restLength: 0f)
        {
            CanBreak = false
        };

        Assert.Equal(0.5f, link.RestLength);
        Assert.False(link.BeginSubstep(1f / 120f));
        link.Solve(1f / 120f);
        Assert.False(link.Broken);
    }

    [Fact]
    public void BossBurstFadeAndCombatReleaseEndOnTheLastVideoFrame()
    {
        Assert.Equal(0.9f, BossBurstTimeline.LeadSeconds);
        Assert.Equal(0.2f, BossBurstTimeline.WhiteoutSeconds);
        Assert.Equal(0.7f, BossBurstTimeline.WhiteoutStartSeconds);
        Assert.Equal(0f, BossBurstTimeline.ResolveWhiteoutMix(0.7f));
        Assert.Equal(0.5f, BossBurstTimeline.ResolveWhiteoutMix(0.8f), 5);
        Assert.Equal(1f, BossBurstTimeline.ResolveWhiteoutMix(0.9f));
        Assert.Equal(1.875f, BossBurstTimeline.VideoSeconds);
        Assert.Equal(1.5f, BossBurstTimeline.FadeStartSeconds);
        Assert.Equal(
            1f,
            BossBurstTimeline.ResolveFadeAlpha(BossBurstTimeline.FadeStartSeconds));
        Assert.InRange(
            BossBurstTimeline.ResolveFadeAlpha(1.75f),
            0f,
            1f);
        Assert.Equal(
            0f,
            BossBurstTimeline.ResolveFadeAlpha(BossBurstTimeline.VideoSeconds));
    }

    private static FinisherForecastOutcome EvaluateForecastForCorrectness<TState>(
        FinisherForecastSimulation<TState, TState> simulation)
        where TState : notnull =>
        FinisherForecastEngine.Evaluate(simulation, maximumSearchTime: TimeSpan.MaxValue);

    private static SoftFragmentBody CreateSoftBody(
        int id,
        BossFragmentPoint center,
        float compressedScale)
    {
        BossFragmentPoint[] hull =
        [
            new(-50f, -50f),
            new(50f, -50f),
            new(50f, 50f),
            new(-50f, 50f)
        ];
        return new SoftFragmentBody(
            id,
            new SoftBodyBounds(-50f, -50f, 100f, 100f),
            hull,
            center,
            compressedScale,
            mass: 1f);
    }

    private static float SmoothStep(float start, float end, float value)
    {
        float progress = Math.Clamp((value - start) / Math.Max(0.0001f, end - start), 0f, 1f);
        return progress * progress * (3f - 2f * progress);
    }

    private static SoftFragmentBody CreateMirroredSoftBody()
    {
        var grid = new BossFragmentPoint[SoftFragmentBody.ParticleCount];
        for (int row = 0; row < SoftFragmentBody.GridSize; row++)
        {
            for (int column = 0; column < SoftFragmentBody.GridSize; column++)
            {
                float u = column / (float)(SoftFragmentBody.GridSize - 1);
                float v = row / (float)(SoftFragmentBody.GridSize - 1);
                grid[row * SoftFragmentBody.GridSize + column] = new BossFragmentPoint(
                    50f - 100f * u,
                    -50f + 100f * v);
            }
        }

        SoftBodyHullPoint[] hull =
        [
            new(new BossFragmentPoint(50f, -50f), 0f, 0f),
            new(new BossFragmentPoint(-50f, -50f), 1f, 0f),
            new(new BossFragmentPoint(-50f, 50f), 1f, 1f),
            new(new BossFragmentPoint(50f, 50f), 0f, 1f)
        ];
        return new SoftFragmentBody(
            id: 3,
            grid,
            hull,
            center: default,
            compressedScale: 1f,
            mass: 1f,
            collisionMargin: 18f);
    }

#if false
    private static BossSemanticPartDefinition CreateSemanticPart(
        int index,
        float x,
        float y,
        float width,
        float height,
        float areaRatio,
        ulong boneId,
        IReadOnlyList<ulong>? ancestors = null,
        bool detached = false)
    {
        BossFragmentPoint[] hull =
        [
            new(x, y),
            new(x + width, y),
            new(x + width, y + height),
            new(x, y + height)
        ];
        BossFragmentPoint[] alphaSamples = Enumerable.Range(0, 9)
            .Select(sample => new BossFragmentPoint(
                x + width * (0.15f + sample % 3 * 0.35f),
                y + height * (0.15f + sample / 3 * 0.35f)))
            .ToArray();
        return new BossSemanticPartDefinition(
            index,
            boneId,
            $"bone_{boneId}",
            ancestors ?? [],
            new BossFragmentPoint(x + width * 0.5f, y + height * 0.5f),
            new BossFragmentRect(x, y, width, height),
            hull,
            alphaSamples,
            width * height,
            areaRatio,
            detached,
            [index],
            index,
            index);
    }

    private static BossSemanticPartMergeInput CreateMergePart(
        int sourceIndex,
        ulong boneId,
        IReadOnlyList<ulong> ancestors,
        IReadOnlyList<BossFragmentPoint> hull,
        int slotIndex,
        int minimumDrawOrder,
        int maximumDrawOrder,
        float visibleArea,
        bool hasNormalBlendSlot = true)
    {
        BossFragmentRect bounds = BossDismembermentMath.BoundsOf(hull);
        return new BossSemanticPartMergeInput(
            sourceIndex,
            boneId,
            $"bone_{boneId}",
            ancestors,
            BossDismembermentMath.PolygonCentroid(hull),
            BelongsToDetachedPart: false,
            [slotIndex],
            hasNormalBlendSlot,
            minimumDrawOrder,
            maximumDrawOrder,
            visibleArea,
            bounds,
            hull,
            hull);
    }

    private static BossFragmentPoint[] RectangleHull(
        float x,
        float y,
        float width,
        float height) =>
    [
        new(x, y),
        new(x + width, y),
        new(x + width, y + height),
        new(x, y + height)
    ];
#endif

    private static FinisherForecastSimulation<ForecastTestState, ForecastTestState> CreateForecast(
        IReadOnlyList<ForecastTestState> states,
        int hits,
        FinisherForecastTargeting targeting,
        int damage,
        bool useKarate = false,
        int narakuSplash = 0,
        bool unknownEffect = false,
        int? singleTarget = null)
    {
        return new FinisherForecastSimulation<ForecastTestState, ForecastTestState>(
            states,
            hits,
            targeting,
            state => state.Hp > 0,
            state => state,
            (current, targets, _) =>
            {
                if (unknownEffect)
                {
                    return false;
                }

                foreach (int target in targets)
                {
                    ForecastTestState state = current[target];
                    int blocked = Math.Min(state.Block, damage);
                    int primaryLoss = damage - blocked;
                    state = state with { Block = state.Block - blocked, Hp = state.Hp - primaryLoss };
                    if (useKarate && damage > 0 && state.Hp > 0 && state.Karate > 0)
                    {
                        state = state with { Hp = state.Hp - state.Karate };
                        if (state.Hp > 0)
                        {
                            state = state with { Karate = state.Karate - 1 };
                        }
                    }
                    current[target] = state;

                    if (narakuSplash > 0)
                    {
                        for (int enemy = 0; enemy < current.Length; enemy++)
                        {
                            if (current[enemy].Hp > 0)
                            {
                                current[enemy] = current[enemy] with { Hp = current[enemy].Hp - narakuSplash };
                            }
                        }
                    }
                }

                return true;
            },
            singleTarget);
    }

    private static FinisherForecastSimulation<ForecastTestState, ForecastTestState>
        CreateRandomHitThenAllEffectForecast(
            IReadOnlyList<ForecastTestState> states,
            int randomDamage,
            int allDamage,
            int hitCount = 1)
    {
        FinisherForecastPostEffect<ForecastTestState>[] effects =
        [
            new(
                FinisherForecastEffectTargeting.All,
                (current, targets) =>
                {
                    foreach (int target in targets)
                    {
                        current[target] = current[target] with
                        {
                            Hp = current[target].Hp - allDamage
                        };
                    }

                    return true;
                })
        ];
        return new FinisherForecastSimulation<ForecastTestState, ForecastTestState>(
            states,
            hitCount,
            FinisherForecastTargeting.Random,
            state => state.Hp > 0,
            state => state,
            (current, targets, _) =>
            {
                foreach (int target in targets)
                {
                    current[target] = current[target] with
                    {
                        Hp = current[target].Hp - randomDamage
                    };
                }

                return true;
            },
            PostEffects: effects);
    }

    private readonly record struct ForecastTestState(int Hp, int Block, int Karate);
}
