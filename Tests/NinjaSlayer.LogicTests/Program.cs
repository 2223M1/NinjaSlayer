using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Lifecycle;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

Check(KarateDamageMath.CumulativeDamage(5, 0) == 0, "Zero hits must deal zero Karate damage.");
Check(KarateDamageMath.CumulativeDamage(5, 2) == 9, "Karate must use descending arithmetic damage.");
Check(KarateDamageMath.CumulativeDamage(3, 10) == 6, "Karate triggers cannot exceed the stack count.");

var memo = new BoundedMemoSearch<string, bool>(2, TimeSpan.FromSeconds(1));
Check(memo.Lookup("state-a", out _) == MemoSearchLookup.NewState, "The first state must be visited.");
memo.Store("state-a", true);
Check(memo.Lookup("state-a", out bool cached) == MemoSearchLookup.Cached && cached,
    "An identical forecast state must use the memoized result.");
Check(memo.VisitedStates == 1, "A cache hit must not consume the state budget.");
Check(memo.Lookup("state-b", out _) == MemoSearchLookup.NewState, "The second state must fit the budget.");
Check(memo.Lookup("state-c", out _) == MemoSearchLookup.BudgetExceeded,
    "Forecasting must fail closed after the state budget is exhausted.");

object metricsPlayer = new();
var metrics = new CombatMetricsSnapshot<object>(1, 0);
metrics.AddGeneratedChado(metricsPlayer);
metrics.MarkChadoDiscarded(metricsPlayer);
metrics.MarkChadoExhausted(metricsPlayer);
metrics.MarkHpLost(metricsPlayer);
metrics.AddFinishedCard(metricsPlayer, isAttack: true, isMelee: true);
Check(metrics.GeneratedChado(metricsPlayer) == 1
      && metrics.ChadoDiscarded(metricsPlayer)
      && metrics.ChadoExhausted(metricsPlayer)
      && metrics.LostHp(metricsPlayer)
      && metrics.PreviousFinishedWasAttack(metricsPlayer)
      && metrics.MeleeAttacks(metricsPlayer) == 1,
    "Combat metrics must update incrementally within one turn.");
metrics.EnsureTurn(2, 0);
Check(metrics.GeneratedChado(metricsPlayer) == 1
      && !metrics.ChadoDiscarded(metricsPlayer)
      && !metrics.ChadoExhausted(metricsPlayer)
      && !metrics.LostHp(metricsPlayer)
      && metrics.PreviousFinishedWasAttack(metricsPlayer)
      && metrics.MeleeAttacks(metricsPlayer) == 0,
    "Turn rollover must reset only turn-scoped metrics.");

static FinisherForecastSimulation<ForecastTestState> CreateForecast(
    IReadOnlyList<ForecastTestState> states,
    int hits,
    FinisherForecastTargeting targeting,
    int damage,
    bool useKarate = false,
    int narakuSplash = 0,
    bool unknownEffect = false,
    int? singleTarget = null)
{
    return new FinisherForecastSimulation<ForecastTestState>(
        states,
        hits,
        targeting,
        state => state.Hp > 0,
        state => $"{state.Hp},{state.Block},{state.Karate}",
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

Check(FinisherForecastEngine.Evaluate(CreateForecast(
          [new ForecastTestState(5, 0, 0)], 1, FinisherForecastTargeting.Single, 5, singleTarget: 0))
      == FinisherForecastOutcome.Guaranteed,
    "A lethal single-target hit must be guaranteed.");
Check(FinisherForecastEngine.Evaluate(CreateForecast(
          [new ForecastTestState(6, 0, 0)], 1, FinisherForecastTargeting.Single, 5, singleTarget: 0))
      == FinisherForecastOutcome.NotGuaranteed,
    "A nonlethal single-target hit must fail closed.");
Check(FinisherForecastEngine.Evaluate(CreateForecast(
          [new ForecastTestState(5, 0, 0), new ForecastTestState(5, 0, 0)],
          1, FinisherForecastTargeting.All, 5))
      == FinisherForecastOutcome.Guaranteed,
    "A lethal AOE hit must clear every enemy.");
Check(FinisherForecastEngine.Evaluate(CreateForecast(
          [new ForecastTestState(5, 0, 0), new ForecastTestState(6, 0, 0)],
          1, FinisherForecastTargeting.All, 5))
      == FinisherForecastOutcome.NotGuaranteed,
    "AOE forecasting must reject a surviving enemy.");
Check(FinisherForecastEngine.Evaluate(CreateForecast(
          [new ForecastTestState(5, 0, 0), new ForecastTestState(5, 0, 0)],
          2, FinisherForecastTargeting.Random, 5))
      == FinisherForecastOutcome.Guaranteed,
    "Random forecasting must accept only when every target allocation clears combat.");
Check(FinisherForecastEngine.Evaluate(CreateForecast(
          [new ForecastTestState(5, 0, 0), new ForecastTestState(5, 0, 0)],
          1, FinisherForecastTargeting.Random, 5))
      == FinisherForecastOutcome.NotGuaranteed,
    "Random forecasting must reject any allocation that leaves a survivor.");
Check(FinisherForecastEngine.Evaluate(CreateForecast(
          [new ForecastTestState(5, 3, 0)], 1, FinisherForecastTargeting.Single, 8, singleTarget: 0))
      == FinisherForecastOutcome.Guaranteed,
    "Forecasting must consume block before HP.");
Check(FinisherForecastEngine.Evaluate(CreateForecast(
          [new ForecastTestState(7, 0, 3)], 2, FinisherForecastTargeting.Single, 1,
          useKarate: true, singleTarget: 0))
      == FinisherForecastOutcome.Guaranteed,
    "Forecasting must apply descending Karate damage after each powered hit.");
Check(FinisherForecastEngine.Evaluate(CreateForecast(
          [new ForecastTestState(1, 0, 0), new ForecastTestState(2, 0, 0)],
          1, FinisherForecastTargeting.Random, 1, narakuSplash: 2))
      == FinisherForecastOutcome.Guaranteed,
    "Naraku splash must be included in every random branch.");
Check(FinisherForecastEngine.Evaluate(CreateForecast(
          [new ForecastTestState(1, 0, 0)], 1, FinisherForecastTargeting.Single, 1,
          unknownEffect: true, singleTarget: 0))
      == FinisherForecastOutcome.NotGuaranteed,
    "Unknown forecast effects must fail closed.");
Check(FinisherForecastEngine.Evaluate(
          CreateForecast(
              [new ForecastTestState(10, 0, 0), new ForecastTestState(10, 0, 0), new ForecastTestState(10, 0, 0)],
              20,
              FinisherForecastTargeting.Random,
              0),
          maximumSearchStates: 1,
          maximumSearchTime: TimeSpan.FromSeconds(1))
      == FinisherForecastOutcome.IndeterminateBudget,
    "Random forecasting must report budget exhaustion instead of guessing.");

Check(!XAttackAudioContext.SuppressAutomaticSfx, "X attack SFX must start unsuppressed.");
using (XAttackAudioContext.Suppress())
{
    Check(XAttackAudioContext.SuppressAutomaticSfx, "The outer suppression scope must be active.");
    using (XAttackAudioContext.Suppress())
    {
        Check(XAttackAudioContext.SuppressAutomaticSfx, "Nested suppression must remain active.");
    }
    Check(XAttackAudioContext.SuppressAutomaticSfx, "Disposing an inner scope must preserve the outer scope.");
    Check(await Task.Run(() => XAttackAudioContext.SuppressAutomaticSfx),
        "Suppression depth must flow across awaited work.");
}
Check(!XAttackAudioContext.SuppressAutomaticSfx, "The final scope must restore automatic SFX.");
Check(CinematicTimingContract.BossMinimumCameraHoldSeconds == 2f,
    "Boss framing must hold for at least two active seconds.");
Check(CinematicTimingContract.BossCameraReturnSeconds == 0.2f
      && CinematicTimingContract.FinisherReturnSeconds == 0.2f,
    "Boss and finisher camera restoration timing must remain calibrated.");
Check(CinematicTimingContract.FinisherWatchdogSeconds == 90f,
    "The finisher watchdog must retain its non-paused recovery budget.");
var cinematicLifetime = new CinematicSessionLifetime();
CancellationToken cinematicToken = cinematicLifetime.Token;
cinematicLifetime.Cancel();
Check(cinematicToken.IsCancellationRequested,
    "Cinematic cancellation must reach every task sharing the session token.");
cinematicLifetime.Dispose();
cinematicLifetime.Dispose();
Check(cinematicLifetime.IsDisposed,
    "Cinematic resource disposal must be idempotent.");

object subject = new();
object outerScope = new();
object innerScope = new();
object stateOwner = new();
var scopes = new ResolutionScopeRegistry<object, object>();
scopes.Begin(subject, outerScope);
scopes.GetOrCreateState(outerScope, stateOwner, static () => new List<int>()).Add(1);
scopes.Begin(subject, innerScope);
Check(scopes.TryGetLatestScope(subject, out object? latest) && ReferenceEquals(latest, innerScope),
    "Nested resolution must expose the innermost scope.");
scopes.Complete(innerScope);
Check(scopes.TryGetLatestScope(subject, out latest) && ReferenceEquals(latest, outerScope),
    "Completing an inner resolution must restore the outer scope.");
Check(scopes.TryGetState(outerScope, stateOwner, out List<int>? scopedValues)
      && scopedValues is not null
      && scopedValues.SequenceEqual([1]),
    "State must remain isolated on its owning resolution.");
scopes.CompleteSubject(subject);
Check(scopes.Count == 0 && !scopes.TryGetLatestScope(subject, out _),
    "Subject cleanup must release every active resolution after an exception.");

Console.WriteLine("NinjaSlayer pure logic checks passed.");

internal readonly record struct ForecastTestState(int Hp, int Block, int Karate);
