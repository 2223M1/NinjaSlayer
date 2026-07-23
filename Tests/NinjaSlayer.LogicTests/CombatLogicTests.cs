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

    private readonly record struct ForecastTestState(int Hp, int Block, int Karate);
}
