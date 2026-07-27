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
    public void BossDismembermentUsesMoreFragmentsForLargeBodiesAndCapsDetachedParts()
    {
        Assert.Equal(8, BossDismembermentMath.ResolvePieceCount(360f, 360f, 20, detachedPart: false));
        Assert.Equal(12, BossDismembermentMath.ResolvePieceCount(720f, 720f, 20, detachedPart: false));
        Assert.Equal(6, BossDismembermentMath.ResolvePieceCount(360f, 360f, 20, detachedPart: true));
        Assert.Equal(2, BossDismembermentMath.ResolvePieceCount(360f, 360f, 2, detachedPart: true));
        Assert.Equal(16, BossDismembermentMath.ResolveSpinePieceCount(24, detachedPart: false));
        Assert.Equal(6, BossDismembermentMath.ResolveSpinePieceCount(24, detachedPart: true));
        Assert.Equal(5, BossDismembermentMath.ResolveSpinePieceCount(5, detachedPart: false));
        Assert.Equal(
            new BossFragmentAllocation(10, 6),
            BossDismembermentMath.AllocateSpinePieces(24, 24));
        Assert.Equal(
            new BossFragmentAllocation(12, 4),
            BossDismembermentMath.AllocateSpinePieces(12, 4));
    }

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
    public void BossDismembermentBurstDirectionsCoverTheFullCircle()
    {
        BossFragmentPoint[] directions = Enumerable.Range(0, 16)
            .Select(index => BossDismembermentMath.ResolveBurstDirection(
                index,
                16,
                rotationRadians: 0f,
                jitter: 0f))
            .ToArray();

        Assert.Contains(directions, direction => direction.X < -0.9f);
        Assert.Contains(directions, direction => direction.X > 0.9f);
        Assert.Contains(directions, direction => direction.Y < -0.9f);
        Assert.Contains(directions, direction => direction.Y > 0.9f);
        Assert.InRange(MathF.Abs(directions.Sum(direction => direction.X)), 0f, 0.001f);
        Assert.InRange(MathF.Abs(directions.Sum(direction => direction.Y)), 0f, 0.001f);
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
    public void BossDismembermentLaunchesOutwardAndSmallerPiecesFaster()
    {
        BossFragmentLaunch small = BossDismembermentMath.ResolveLaunch(
            new BossFragmentPoint(100f, 0f),
            new BossFragmentPoint(0f, 0f),
            areaRatio: 0.3f,
            randomA: 0.25f,
            randomB: 0.75f);
        BossFragmentLaunch large = BossDismembermentMath.ResolveLaunch(
            new BossFragmentPoint(100f, 0f),
            new BossFragmentPoint(0f, 0f),
            areaRatio: 2f,
            randomA: 0.25f,
            randomB: 0.75f);

        Assert.True(small.VelocityX > 0f);
        Assert.True(MathF.Sqrt(small.VelocityX * small.VelocityX + small.VelocityY * small.VelocityY)
            > MathF.Sqrt(large.VelocityX * large.VelocityX + large.VelocityY * large.VelocityY));
        Assert.True(MathF.Abs(small.AngularVelocityDegrees) > MathF.Abs(large.AngularVelocityDegrees));
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

        Assert.True(body.ResolveAreaRatio() > 0.8f);
        Assert.True(body.ResolveMinimumCellAreaRatio() > 0f);
    }

    [Fact]
    public void BossSoftBodyGravityIsDownwardAndAirDragDissipatesHorizontalSpeed()
    {
        SoftFragmentBody body = CreateSoftBody(0, default, 1f);
        body.Release(new BossFragmentPoint(100f, -20f), 0f);
        body.Predict(0.1f, gravity: 200f, airDrag: 2f);

        BossFragmentPoint velocity = body.GetParticleVelocity(0);
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
    public void BossSoftBodyCollisionSeparatesOverlappingBodiesAndBroadphaseDeduplicatesPairs()
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
        float before = MathF.Abs(second.Center.X - first.Center.X);
        for (int step = 0; step < 8; step++)
        {
            solver.Step([first, second], [], 1f / 60f, gravity: 0f, airDrag: 0.6f);
        }

        Assert.True(MathF.Abs(second.Center.X - first.Center.X) > before);
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

        new BossSoftBodySolver().Step(
            [first, second],
            [],
            1f / 60f,
            gravity: 0f,
            airDrag: 0f);

        float relativeHorizontalSpeed = second.CenterVelocity.X - first.CenterVelocity.X;
        Assert.InRange(relativeHorizontalSpeed, 1f, 360f);
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
        Assert.False(link.Solve(1f / 120f));
        Assert.False(link.Broken);
    }

    [Fact]
    public void BossBurstFadeAndCombatReleaseEndOnTheLastVideoFrame()
    {
        Assert.Equal(0.9f, BossBurstTimeline.LeadSeconds);
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
