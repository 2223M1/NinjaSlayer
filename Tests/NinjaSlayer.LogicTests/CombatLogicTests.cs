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
        Assert.Equal(5, BossDismembermentMath.ResolvePieceCount(360f, 360f, 20, detachedPart: false));
        Assert.Equal(8, BossDismembermentMath.ResolvePieceCount(720f, 720f, 20, detachedPart: false));
        Assert.Equal(4, BossDismembermentMath.ResolvePieceCount(360f, 360f, 20, detachedPart: true));
        Assert.Equal(2, BossDismembermentMath.ResolvePieceCount(360f, 360f, 2, detachedPart: true));
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
        Assert.True(small.VelocityY < 0f);
        Assert.True(MathF.Sqrt(small.VelocityX * small.VelocityX + small.VelocityY * small.VelocityY)
            > MathF.Sqrt(large.VelocityX * large.VelocityX + large.VelocityY * large.VelocityY));
        Assert.True(MathF.Abs(small.AngularVelocityDegrees) > MathF.Abs(large.AngularVelocityDegrees));
    }

    private static FinisherForecastOutcome EvaluateForecastForCorrectness<TState>(
        FinisherForecastSimulation<TState, TState> simulation)
        where TState : notnull =>
        FinisherForecastEngine.Evaluate(simulation, maximumSearchTime: TimeSpan.MaxValue);

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
