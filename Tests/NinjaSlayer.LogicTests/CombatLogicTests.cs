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
    public void FinisherForecastTargetsSecondariesWithoutRequiringTheirDeath()
    {
        List<int> allTargets = [];
        var allSimulation = new FinisherForecastSimulation<ForecastTestState, ForecastTestState>(
            [new(5, 0, 0), new(50, 0, 0, IsPrimary: false)],
            1,
            FinisherForecastTargeting.All,
            state => state.Hp > 0,
            state => state,
            (current, targets, _) =>
            {
                allTargets.AddRange(targets);
                foreach (int target in targets)
                {
                    current[target] = current[target] with { Hp = current[target].Hp - 5 };
                }

                return true;
            },
            IsVictoryBlocking: state => state.Hp > 0 && state.IsPrimary);

        Assert.Equal(
            FinisherForecastOutcome.Guaranteed,
            EvaluateForecastForCorrectness(allSimulation));
        Assert.Equal([0, 1], allTargets);
        Assert.Equal(
            FinisherForecastOutcome.Guaranteed,
            EvaluateForecastForCorrectness(CreateForecast(
                [new(5, 0, 0), new(50, 0, 0, IsPrimary: false)],
                1,
                FinisherForecastTargeting.Single,
                5,
                singleTarget: 0)));
        Assert.Equal(
            FinisherForecastOutcome.NotGuaranteed,
            EvaluateForecastForCorrectness(CreateForecast(
                [new(6, 0, 0), new(5, 0, 0, IsPrimary: false)],
                1,
                FinisherForecastTargeting.Single,
                5,
                singleTarget: 0)));
        Assert.Equal(
            FinisherForecastOutcome.NotGuaranteed,
            EvaluateForecastForCorrectness(CreateForecast(
                [new(5, 0, 0), new(5, 0, 0, IsPrimary: false)],
                1,
                FinisherForecastTargeting.Random,
                5)));
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
        Assert.Equal(0f, FinisherActionTrajectory.FastProgress(-1f));
        Assert.Equal(0.5f, FinisherActionTrajectory.FastProgress(0.5f), 5);
        Assert.Equal(1f, FinisherActionTrajectory.FastProgress(2f));

        Assert.Equal(120f, FinisherActionTrajectory.SlowTravelPixels);
        Assert.Equal(0.2f, FinisherActionTrajectory.SlowTravelSeconds);
        Assert.Equal(0f, FinisherActionTrajectory.SlowProgress(-1f));
        Assert.Equal(MathF.Pow(0.5f, 10f), FinisherActionTrajectory.SlowProgress(0.5f), 8);
        Assert.Equal(1f, FinisherActionTrajectory.SlowProgress(2f));
    }

    [Theory]
    [InlineData(0f, 450f, 1f, 330f)]
    [InlineData(500f, 50f, -1f, 170f)]
    public void YamotoKokiFinisherKeepsItsContinuousPreparationLunge(
        float actorX,
        float impactX,
        float fallbackDirection,
        float expectedStartX)
    {
        float startX = FinisherActionTrajectory.ResolveIaiStartX(
            actorX,
            impactX,
            fallbackDirection);

        Assert.Equal(expectedStartX, startX);
        Assert.Equal(FinisherActionTrajectory.SlowTravelPixels, MathF.Abs(impactX - startX));
    }

    [Theory]
    [InlineData(200f, 100f, true)]
    [InlineData(100.6f, 100f, true)]
    [InlineData(100.5f, 100f, false)]
    [InlineData(90f, 100f, false)]
    public void FinisherLeapOnlyAlignsNinjaSlayerUpward(
        float actorCenterY,
        float targetCenterY,
        bool expected)
    {
        Assert.Equal(expected, FinisherLeapTrajectory.ShouldAlign(actorCenterY, targetCenterY));
    }

    [Theory]
    [InlineData(100f, 200f, 6f)]
    [InlineData(200f, 100f, -6f)]
    public void FinisherLeapTiltsTowardTheFocusedVictim(
        float actorCenterX,
        float targetCenterX,
        float expectedDegrees)
    {
        Assert.Equal(
            expectedDegrees,
            FinisherLeapTrajectory.ResolveTiltDegrees(actorCenterX, targetCenterX));
    }

    [Theory]
    [InlineData(40f, 10f)]
    [InlineData(200f, 24f)]
    public void FinisherLeapReturnAddsAClampedUpwardArc(float liftDistance, float expectedArcHeight)
    {
        float arcHeight = FinisherLeapTrajectory.ResolveArcHeight(liftDistance);

        Assert.Equal(expectedArcHeight, arcHeight);
        Assert.Equal(0f, FinisherLeapTrajectory.ResolveReturnArcFactor(0f));
        Assert.Equal(0f, FinisherLeapTrajectory.ResolveReturnArcFactor(1f));
        Assert.Equal(1f, FinisherLeapTrajectory.ResolveReturnArcFactor(0.5f));
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


    [Theory]
    [InlineData(-1f, 0f, -200f)]
    [InlineData(1f, 0.5f, 300f)]
    [InlineData(0f, 1f, 400f)]
    public void ArchitectLeadVelocityUsesTheImpactDirectionAndTrialSpeedRange(
        float impactDirection,
        float unitSample,
        float expectedX)
    {
        BossFragmentPoint velocity = BossDismembermentMath.ResolveArchitectLeadVelocity(
            impactDirection,
            unitSample);

        Assert.Equal(expectedX, velocity.X, 5);
        Assert.Equal(0f, velocity.Y);
    }

    [Fact]
    public void ArchitectRagdollTopologyUsesTheNearestVisibleAncestorAndLargestTopLevelRoot()
    {
        ArchitectRagdollTopologyPart[] parts =
        [
            new(1UL, 4f, 10, [90UL]),
            new(2UL, 1f, 20, [70UL, 1UL, 90UL]),
            new(3UL, 0.5f, 30, [2UL, 1UL, 90UL]),
            new(4UL, 2f, 5, [80UL])
        ];

        IReadOnlyList<ArchitectRagdollTopologyNode> topology =
            BossDismembermentMath.ResolveArchitectRagdollTopology(parts);

        Assert.Equal(1UL, topology[0].BoneId);
        Assert.Null(topology[0].ParentBoneId);
        Assert.Equal(1UL, topology.Single(node => node.BoneId == 2UL).ParentBoneId);
        Assert.Equal(2UL, topology.Single(node => node.BoneId == 3UL).ParentBoneId);
        Assert.Equal(1UL, topology.Single(node => node.BoneId == 4UL).ParentBoneId);
    }

    [Fact]
    public void ArchitectBoneRotationAppliesOnlyTheRelativeSegmentRotation()
    {
        float originalRotation = 17f;
        float parentRotation = 0.25f;
        float childRotation = 0.6f;

        float result = BossDismembermentMath.ResolveArchitectLocalBoneRotation(
            originalRotation,
            childRotation,
            parentRotation);

        Assert.Equal(
            originalRotation + (childRotation - parentRotation) * (180f / MathF.PI),
            result,
            5);
        Assert.Equal(
            originalRotation,
            BossDismembermentMath.ResolveArchitectLocalBoneRotation(
                originalRotation,
                parentRotation,
                parentRotation),
            5);
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
        for (int step = 0; step < 8; step++)
        {
            solver.Step(
                [first, second],
                [],
                1f / 60f,
                gravity: 0f,
                airDrag: 0.6f);
        }

        Assert.InRange(
            MathF.Abs(second.Center.X - first.Center.X),
            openingDistance - 0.01f,
            openingDistance + 0.01f);

        for (int index = 0; index < SoftFragmentBody.ParticleCount; index++)
        {
            second.ApplyParticleCorrection(index, new BossFragmentPoint(180f, 0f));
        }

        solver.Step([first, second], [], 1f / 60f, gravity: 0f, airDrag: 0f);
        first.Release(new BossFragmentPoint(240f, 0f), 0f);
        second.Release(new BossFragmentPoint(-240f, 0f), 0f);
        bool collided = false;
        for (int step = 0; step < 40 && !collided; step++)
        {
            solver.Step(
                [first, second],
                [],
                1f / 60f,
                gravity: 0f,
                airDrag: 0f);
            collided = second.CenterVelocity.X - first.CenterVelocity.X >= 0f;
        }

        Assert.True(collided);
        float contactDistance = MathF.Abs(second.Center.X - first.Center.X);
        for (int step = 0; step < 60; step++)
        {
            solver.Step(
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

    [Theory]
    [InlineData(-1f)]
    [InlineData(1f)]
    public void ArchitectRagdollTorsoPullsConnectedLimbsBeforeTheBurst(float impactDirection)
    {
        SoftFragmentBody torso = CreateSoftBody(
            1,
            new SoftBodyBounds(-30f, -120f, 60f, 90f));
        SoftFragmentBody leftArm = CreateSoftBody(
            2,
            new SoftBodyBounds(-75f, -105f, 45f, 25f));
        SoftFragmentBody rightArm = CreateSoftBody(
            3,
            new SoftBodyBounds(30f, -105f, 45f, 25f));
        SoftFragmentBody leftLeg = CreateSoftBody(
            4,
            new SoftBodyBounds(-25f, -30f, 20f, 70f));
        SoftFragmentBody rightLeg = CreateSoftBody(
            5,
            new SoftBodyBounds(5f, -30f, 20f, 70f));
        SoftFragmentBody[] bodies = [torso, leftArm, rightArm, leftLeg, rightLeg];
        BossFragmentPoint velocity = BossDismembermentMath.ResolveArchitectLeadVelocity(
            impactDirection,
            unitSample: 0.5f);
        foreach (SoftFragmentBody body in bodies)
        {
            body.SetMaterial(SoftBodyMaterialProfile.ArchitectLead);
            body.SetCollisionEnvelope(hullScale: 1f, marginScale: 0f);
        }

        torso.Release(velocity, angularVelocityRadians: 0f);
        foreach (SoftFragmentBody limb in bodies.Skip(1))
        {
            limb.Release(default, angularVelocityRadians: 0f);
        }

        Assert.InRange(MathF.Abs(torso.CenterVelocity.X), 200f, 400f);
        Assert.Equal(MathF.Sign(impactDirection), MathF.Sign(torso.CenterVelocity.X));
        Assert.Equal(0f, torso.CenterVelocity.Y, 5);
        Assert.All(Enumerable.Range(0, SoftFragmentBody.ParticleCount), index =>
        {
            Assert.Equal(velocity.X, torso.GetParticleVelocity(index).X, 5);
            Assert.Equal(0f, torso.GetParticleVelocity(index).Y, 5);
        });
        Assert.All(bodies.Skip(1), limb =>
        {
            Assert.Equal(0f, limb.CenterVelocity.X, 5);
            Assert.Equal(0f, limb.CenterVelocity.Y, 5);
            Assert.All(Enumerable.Range(0, SoftFragmentBody.ParticleCount), index =>
            {
                Assert.Equal(0f, limb.GetParticleVelocity(index).X, 5);
                Assert.Equal(0f, limb.GetParticleVelocity(index).Y, 5);
            });
        });

        SoftRagdollLink[] links =
        [
            CreateRagdollLink(torso, leftArm, new BossFragmentPoint(-30f, -95f)),
            CreateRagdollLink(torso, rightArm, new BossFragmentPoint(30f, -95f)),
            CreateRagdollLink(torso, leftLeg, new BossFragmentPoint(-15f, -30f)),
            CreateRagdollLink(torso, rightLeg, new BossFragmentPoint(15f, -30f))
        ];
        Dictionary<SoftFragmentBody, BossFragmentPoint> initialOffsets = bodies
            .Skip(1)
            .ToDictionary(
                body => body,
                body => Subtract(body.Center, torso.Center));
        float torsoStartX = torso.Center.X;
        var solver = new BossSoftBodySolver();
        for (int step = 0; step < 54; step++)
        {
            solver.Step(
                bodies,
                links,
                1f / 60f,
                gravity: 860f,
                airDrag: 0.08f,
                floorY: 40f,
                centerSpeedLimit: BossSoftBodySolver.DefaultMaximumCenterSpeed);
        }

        Dictionary<SoftFragmentBody, SoftBodyRenderPose> poses = bodies.ToDictionary(
            body => body,
            body => ResolvePose(body));
        Assert.True(
            impactDirection * (torso.Center.X - torsoStartX) > 0f,
            $"The Architect torso did not move in the impact direction: "
                + $"start={torsoStartX:F3}, end={torso.Center.X:F3}.");
        int articulatedChildren = bodies.Skip(1).Count(body =>
        {
            BossFragmentPoint currentOffset = Subtract(body.Center, torso.Center);
            BossFragmentPoint offsetDelta = Subtract(currentOffset, initialOffsets[body]);
            float displacement = Length(offsetDelta);
            float relativeRotation = MathF.Abs(
                poses[body].RotationRadians - poses[torso].RotationRadians);
            return displacement > 2f || relativeRotation > 0.02f;
        });
        Assert.True(
            articulatedChildren >= 2,
            $"Only {articulatedChildren} Architect limbs moved relative to the torso.");
        Assert.All(links, link =>
        {
            Assert.False(link.Broken);
            float jointDistance = Length(Subtract(
                link.Second.GetParticlePosition(link.SecondParticle),
                link.First.GetParticlePosition(link.FirstParticle)));
            float tolerance = Math.Max(
                2f,
                Math.Min(link.First.ShortDimension, link.Second.ShortDimension) * 0.05f);
            Assert.True(
                jointDistance <= link.RestLength + tolerance,
                $"Joint stretched to {jointDistance:F3} from {link.RestLength:F3}.");
        });
        Assert.All(bodies, body =>
        {
            Assert.True(body.HasFiniteState);
            Assert.True(body.ResolveMinimumCellAreaRatio() > 0f);
        });
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
        float energyBefore = first.ResolveKineticEnergy() + second.ResolveKineticEnergy();
        float maximumCenterSpeed = 0f;
        float relativeHorizontalSpeed = float.NegativeInfinity;
        for (int frame = 0; frame < 60; frame++)
        {
            solver.Step(
                [first, second],
                [],
                1f / 60f,
                gravity: 0f,
                airDrag: 0f);
            maximumCenterSpeed = Math.Max(
                maximumCenterSpeed,
                Math.Max(first.ResolveCenterSpeed(), second.ResolveCenterSpeed()));
            relativeHorizontalSpeed = second.CenterVelocity.X - first.CenterVelocity.X;
            if (relativeHorizontalSpeed >= 0f)
            {
                break;
            }
        }

        float energyAfter = first.ResolveKineticEnergy() + second.ResolveKineticEnergy();
        Assert.True(
            relativeHorizontalSpeed is >= 1f and <= 360f,
            $"relative={relativeHorizontalSpeed:F3}, "
                + $"energy_before={energyBefore:F3}, energy_after={energyAfter:F3}");
        Assert.True(energyAfter <= energyBefore * 1.011f + 1f);
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

    private static SoftRagdollLink CreateRagdollLink(
        SoftFragmentBody parent,
        SoftFragmentBody child,
        BossFragmentPoint pivot)
    {
        int parentParticle = FindNearestParticle(parent, pivot);
        int childParticle = FindNearestParticle(child, pivot);
        return new SoftRagdollLink(
            parent,
            parentParticle,
            child,
            childParticle,
            Length(Subtract(
                child.GetParticlePosition(childParticle),
                parent.GetParticlePosition(parentParticle))))
        {
            CanBreak = false
        };
    }

    private static int FindNearestParticle(
        SoftFragmentBody body,
        BossFragmentPoint pivot)
    {
        return Enumerable.Range(0, SoftFragmentBody.ParticleCount)
            .OrderBy(index => Length(Subtract(body.GetParticlePosition(index), pivot)))
            .First();
    }

    private static SoftBodyRenderPose ResolvePose(SoftFragmentBody body)
    {
        var residuals = new BossFragmentPoint[SoftFragmentBody.ParticleCount];
        Assert.True(SoftBodyRenderPoseResolver.TryResolve(
            body,
            previousRotation: 0f,
            residuals,
            out SoftBodyRenderPose pose));
        return pose;
    }

    private static BossFragmentPoint Subtract(
        BossFragmentPoint first,
        BossFragmentPoint second) =>
        new(first.X - second.X, first.Y - second.Y);

    private static float Length(BossFragmentPoint point) =>
        MathF.Sqrt(point.X * point.X + point.Y * point.Y);

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

    private static SoftFragmentBody CreateSoftBody(int id, SoftBodyBounds bounds)
    {
        BossFragmentPoint[] grid = BuildSoftBodyGrid(bounds);
        SoftBodyHullPoint[] hull =
        [
            new(new BossFragmentPoint(bounds.X, bounds.Y), 0f, 0f),
            new(new BossFragmentPoint(bounds.X + bounds.Width, bounds.Y), 1f, 0f),
            new(new BossFragmentPoint(
                bounds.X + bounds.Width,
                bounds.Y + bounds.Height), 1f, 1f),
            new(new BossFragmentPoint(bounds.X, bounds.Y + bounds.Height), 0f, 1f)
        ];
        return new SoftFragmentBody(
            id,
            grid,
            hull,
            bounds.Center,
            compressedScale: 1f,
            mass: 1f,
            collisionMargin: 0f);
    }

    private static BossFragmentPoint[] BuildSoftBodyGrid(SoftBodyBounds bounds)
    {
        var grid = new BossFragmentPoint[SoftFragmentBody.ParticleCount];
        for (int row = 0; row < SoftFragmentBody.GridSize; row++)
        {
            for (int column = 0; column < SoftFragmentBody.GridSize; column++)
            {
                float u = column / (float)(SoftFragmentBody.GridSize - 1);
                float v = row / (float)(SoftFragmentBody.GridSize - 1);
                grid[row * SoftFragmentBody.GridSize + column] = new BossFragmentPoint(
                    bounds.X + bounds.Width * u,
                    bounds.Y + bounds.Height * v);
            }
        }

        return grid;
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
            singleTarget,
            IsVictoryBlocking: state => state.Hp > 0 && state.IsPrimary);
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
            PostEffects: effects,
            IsVictoryBlocking: state => state.Hp > 0 && state.IsPrimary);
    }

    private readonly record struct ForecastTestState(
        int Hp,
        int Block,
        int Karate,
        bool IsPrimary = true);
}
