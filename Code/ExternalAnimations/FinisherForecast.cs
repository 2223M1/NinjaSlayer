using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Content;
using NinjaSlayer.Monsters;
using NinjaSlayer.Powers;
using NinjaSlayer.Scripts;
using static NinjaSlayer.Code.ExternalAnimations.FinisherTimeline;

namespace NinjaSlayer.Code.ExternalAnimations;

internal readonly record struct FinisherForecastResult(int ResolvedHits, bool RequiresAfterCardPlayed);

internal sealed record FinisherForecastEffect(
    decimal Amount,
    ValueProp Props,
    Creature? Dealer,
    CardModel? CardSource,
    CardPlay? CardPlay,
    FinisherForecastEffectTargeting Targeting);

internal static class FinisherForecast
{
    private static readonly FrameScopedCache<FinisherForecastFrameKey, CachedForecast> FrameCache = new();

    public static FinisherForecastOutcome Evaluate(
        Creature owner,
        IReadOnlyList<Creature> enemies,
        FinisherAttackSpec spec,
        AttackCommand? command,
        out FinisherForecastResult result)
    {
        result = default;
        FinisherForecastDescriptor descriptor = spec.Forecast;
        ICombatState? combatState = owner.CombatState;
        IRunState? runState = ResolveRunState(owner);
        if (combatState == null
            || runState == null
            || enemies.Any(enemy => !Hook.ShouldDie(runState, combatState, enemy, out _)))
        {
            return FinisherForecastOutcome.NotGuaranteed;
        }

        int hits = descriptor.HitCount;
        if (command != null)
        {
            hits = (int)Math.Ceiling(Math.Max(0m, Hook.ModifyAttackHitCount(combatState, command, hits)));
        }

        if (hits <= 0)
        {
            return FinisherForecastOutcome.NotGuaranteed;
        }

        var enemyIndices = enemies
            .Select((enemy, index) => (enemy, index))
            .ToDictionary(pair => pair.enemy, pair => pair.index);
        List<FinisherForecastEffect> forecastEffects = [];
        Kusarigama? kusarigama = owner.Player?.GetRelic<Kusarigama>();
        if (kusarigama != null && spec.Card.Type == CardType.Attack)
        {
            int cardsPerTrigger = kusarigama.DynamicVars.Cards.IntValue;
            if (cardsPerTrigger > 0 && kusarigama.DisplayAmount == cardsPerTrigger - 1)
            {
                forecastEffects.Add(new FinisherForecastEffect(
                    kusarigama.DynamicVars.Damage.BaseValue,
                    kusarigama.DynamicVars.Damage.Props,
                    owner,
                    null,
                    null,
                    FinisherForecastEffectTargeting.Random));
            }
        }

        result = new FinisherForecastResult(hits, forecastEffects.Count > 0);
        ForecastState[] states = enemies.Select(enemy => new ForecastState(
            enemy.CurrentHp,
            enemy.Block,
            owner.GetPowerAmount<KaratePower>(),
            enemy.IsPrimaryEnemy)).ToArray();
        Creature? singleTarget = descriptor.SingleTarget ?? spec.CardPlay.Target;
        int? singleTargetIndex = singleTarget != null && enemyIndices.TryGetValue(singleTarget, out int singleIndex)
            ? singleIndex
            : null;
        int[]? fixedTargets = descriptor.FixedTargets?
            .Where(enemyIndices.ContainsKey)
            .Select(target => enemyIndices[target])
            .ToArray();
        FinisherForecastTargeting targeting = descriptor.Targeting switch
        {
            FinisherTargeting.Single => FinisherForecastTargeting.Single,
            FinisherTargeting.All => FinisherForecastTargeting.All,
            FinisherTargeting.Random => FinisherForecastTargeting.Random,
            FinisherTargeting.Fixed => FinisherForecastTargeting.Fixed,
            _ => throw new ArgumentOutOfRangeException(nameof(spec), descriptor.Targeting, null)
        };
        if (targeting == FinisherForecastTargeting.Single && singleTargetIndex == null
            || targeting == FinisherForecastTargeting.Fixed
            && (fixedTargets is not { Length: > 0 } || fixedTargets.Length != descriptor.FixedTargets!.Count))
        {
            return FinisherForecastOutcome.NotGuaranteed;
        }

        decimal[] damageByTarget = enemies.Select(descriptor.Damage).ToArray();
        decimal? narakuHpLoss = owner.GetPower<NarakuPower>() is { } naraku && descriptor.Props.IsPoweredAttack()
            ? naraku.DynamicVars.HpLoss.BaseValue
            : null;
        var cacheKey = new FinisherForecastFrameKey(
            owner,
            spec,
            command,
            enemies,
            damageByTarget,
            narakuHpLoss,
            hits,
            singleTarget,
            forecastEffects);
        ulong frame = Engine.GetProcessFrames();
        if (FrameCache.TryGet(frame, cacheKey, out CachedForecast cached))
        {
            result = cached.Result;
            return cached.Outcome;
        }

        List<FinisherForecastPostEffect<ForecastState>> postCardEffects = forecastEffects
            .Select(effect => new FinisherForecastPostEffect<ForecastState>(
                effect.Targeting,
                (effectStates, targets) =>
                {
                    foreach (int target in targets)
                    {
                        ApplyDamage(
                            owner,
                            enemies,
                            effectStates,
                            target,
                            effect.Amount,
                            effect.Props,
                            effect.Dealer,
                            effect.CardSource,
                            effect.CardPlay);
                    }

                    return true;
                }))
            .ToList();
        var simulation = new FinisherForecastSimulation<ForecastState, ForecastStateKey>(
            states,
            hits,
            targeting,
            state => state.Hp > 0,
            state => new ForecastStateKey(state.Hp, state.Block, state.Karate, state.IsPrimaryEnemy),
            (current, targets, hitIndex) =>
            {
                ApplyHit(owner, enemies, current, spec, damageByTarget, narakuHpLoss, targets, hitIndex);
                return true;
            },
            singleTargetIndex,
            fixedTargets,
            postCardEffects,
            state => state.Hp > 0 && state.IsPrimaryEnemy);
        FinisherForecastOutcome outcome = FinisherForecastEngine.Evaluate(simulation);
        FrameCache.Store(frame, cacheKey, new CachedForecast(outcome, result));
        return outcome;
    }

    public static FinisherForecastOutcome EvaluateAction(
        Creature owner,
        IReadOnlyList<Creature> enemies,
        FinisherActionForecastDescriptor descriptor,
        out FinisherForecastResult result)
    {
        result = new FinisherForecastResult(Math.Max(1, descriptor.HitCount), false);
        ICombatState? combatState = owner.CombatState;
        IRunState? runState = ResolveRunState(owner);
        if (combatState == null
            || runState == null
            || descriptor.HitCount <= 0
            || enemies.Count == 0
            || enemies.Any(enemy => !Hook.ShouldDie(runState, combatState, enemy, out _)))
        {
            return FinisherForecastOutcome.NotGuaranteed;
        }

        var enemyIndices = enemies
            .Select((enemy, index) => (enemy, index))
            .ToDictionary(pair => pair.enemy, pair => pair.index);
        int? singleTargetIndex = descriptor.SingleTarget != null
            && enemyIndices.TryGetValue(descriptor.SingleTarget, out int singleIndex)
                ? singleIndex
                : null;
        int[]? fixedTargets = descriptor.FixedTargets?
            .Where(enemyIndices.ContainsKey)
            .Select(target => enemyIndices[target])
            .ToArray();
        FinisherForecastTargeting targeting = descriptor.Targeting switch
        {
            FinisherTargeting.Single => FinisherForecastTargeting.Single,
            FinisherTargeting.All => FinisherForecastTargeting.All,
            FinisherTargeting.Random => FinisherForecastTargeting.Random,
            FinisherTargeting.Fixed => FinisherForecastTargeting.Fixed,
            _ => throw new ArgumentOutOfRangeException(nameof(descriptor), descriptor.Targeting, null)
        };
        if (targeting == FinisherForecastTargeting.Single && singleTargetIndex == null
            || targeting == FinisherForecastTargeting.Fixed
            && (fixedTargets is not { Length: > 0 }
                || fixedTargets.Length != descriptor.FixedTargets!.Count))
        {
            return FinisherForecastOutcome.NotGuaranteed;
        }

        ForecastState[] states = enemies.Select(enemy => new ForecastState(
            enemy.CurrentHp,
            enemy.Block,
            owner.GetPowerAmount<KaratePower>(),
            enemy.IsPrimaryEnemy)).ToArray();
        decimal[] damageByTarget = enemies.Select(descriptor.Damage).ToArray();
        var simulation = new FinisherForecastSimulation<ForecastState, ForecastStateKey>(
            states,
            descriptor.HitCount,
            targeting,
            state => state.Hp > 0,
            state => new ForecastStateKey(state.Hp, state.Block, state.Karate, state.IsPrimaryEnemy),
            (current, targets, _) =>
            {
                ApplyActionDamage(
                    owner,
                    enemies,
                    current,
                    targets,
                    damageByTarget,
                    descriptor.Props,
                    owner,
                    descriptor.CardSource,
                    descriptor.CardPlay,
                    descriptor.TriggersKarate);

                return true;
            },
            singleTargetIndex,
            fixedTargets,
            IsVictoryBlocking: state => state.Hp > 0 && state.IsPrimaryEnemy);
        return FinisherForecastEngine.Evaluate(simulation);
    }

    public static FinisherForecastOutcome EvaluateYamotoKokiNextTurn(
        Creature owner,
        IReadOnlyList<Creature> enemies,
        IReadOnlyList<Creature> missiles)
    {
        ICombatState? combatState = owner.CombatState;
        IRunState? runState = ResolveRunState(owner);
        if (owner.Monster is not YamotoKokiMonster yamotoKoki
            || combatState == null
            || runState == null
            || enemies.Count == 0
            || enemies.Any(enemy => !Hook.ShouldDie(runState, combatState, enemy, out _))
            || missiles.Any(missile => missile.Monster is not YamotoKokiOrigamiMissile))
        {
            return FinisherForecastOutcome.NotGuaranteed;
        }

        ForecastState[] states = enemies.Select(enemy => new ForecastState(
            enemy.CurrentHp,
            enemy.Block,
            owner.GetPowerAmount<KaratePower>(),
            enemy.IsPrimaryEnemy)).ToArray();
        Creature[] missileDealers = [.. missiles];
        decimal[] missileDamage = missileDealers
            .Select(missile => (decimal)((YamotoKokiOrigamiMissile)missile.Monster!).GetExplodeDamage())
            .ToArray();
        decimal[] iaiDamage = Enumerable.Repeat(
                (decimal)yamotoKoki.GetIaiSlashDamage(),
                enemies.Count)
            .ToArray();
        FinisherForecastPostEffect<ForecastState>[] postEffects =
        [
            new(
                FinisherForecastEffectTargeting.All,
                (current, targets) =>
                {
                    ApplyActionDamage(
                        owner,
                        enemies,
                        current,
                        targets,
                        iaiDamage,
                        ValueProp.Move,
                        owner,
                        cardSource: null,
                        cardPlay: null,
                        triggersKarate: true);
                    return true;
                })
        ];
        var simulation = new FinisherForecastSimulation<ForecastState, ForecastStateKey>(
            states,
            missileDealers.Length,
            FinisherForecastTargeting.Random,
            state => state.Hp > 0,
            state => new ForecastStateKey(state.Hp, state.Block, state.Karate, state.IsPrimaryEnemy),
            (current, targets, hitIndex) =>
            {
                if (hitIndex < 0 || hitIndex >= missileDealers.Length)
                {
                    return false;
                }

                foreach (int targetIndex in targets)
                {
                    ApplyDamage(
                        owner,
                        enemies,
                        current,
                        targetIndex,
                        missileDamage[hitIndex],
                        ValueProp.Move | ValueProp.Unpowered,
                        missileDealers[hitIndex],
                        cardSource: null,
                        cardPlay: null);
                }

                return true;
            },
            PostEffects: postEffects,
            IsVictoryBlocking: state => state.Hp > 0 && state.IsPrimaryEnemy);
        return FinisherForecastEngine.Evaluate(
            simulation,
            branchQuantifier: FinisherForecastBranchQuantifier.AnyBranch);
    }

    private static void ApplyActionDamage(
        Creature owner,
        IReadOnlyList<Creature> enemies,
        ForecastState[] states,
        IReadOnlyList<int> targets,
        decimal[] damageByTarget,
        ValueProp props,
        Creature dealer,
        CardModel? cardSource,
        CardPlay? cardPlay,
        bool triggersKarate)
    {
        List<(int Target, bool TriggerKarate)> primaryResults = [];
        foreach (int targetIndex in targets)
        {
            if (states[targetIndex].Hp <= 0)
            {
                continue;
            }

            bool dealtDamage = ApplyDamage(
                owner,
                enemies,
                states,
                targetIndex,
                damageByTarget[targetIndex],
                props,
                dealer,
                cardSource,
                cardPlay);
            primaryResults.Add((targetIndex, dealtDamage));
        }

        ApplyKarateWave(
            owner,
            enemies,
            states,
            primaryResults,
            props,
            dealer,
            cardSource,
            triggersKarate);
    }

    private static void ApplyHit(
        Creature owner,
        IReadOnlyList<Creature> enemies,
        ForecastState[] states,
        FinisherAttackSpec spec,
        decimal[] damageByTarget,
        decimal? narakuHpLoss,
        IReadOnlyList<int> targets,
        int hitIndex)
    {
        List<(int Target, bool TriggerKarate)> damageResults = [];
        foreach (int targetIndex in targets)
        {
            if (states[targetIndex].Hp <= 0)
            {
                continue;
            }

            Creature target = enemies[targetIndex];
            decimal rawDamage = damageByTarget[targetIndex];
            decimal postHookMultiplier = spec.Card is TornadoFist && hitIndex > 0
                && target.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.VulnerablePower>() <= 0
                    ? 1.5m
                    : 1m;

            bool dealtDamage = ApplyDamage(
                owner,
                enemies,
                states,
                targetIndex,
                rawDamage,
                spec.Forecast.Props,
                owner,
                spec.Card,
                spec.CardPlay,
                postHookMultiplier);
            damageResults.Add((targetIndex, dealtDamage));
        }

        ApplyKarateWave(
            owner,
            enemies,
            states,
            damageResults,
            spec.Forecast.Props,
            owner,
            spec.Card,
            triggersKarate: true);

        for (int resultIndex = 0; resultIndex < damageResults.Count; resultIndex++)
        {
            if (narakuHpLoss.HasValue)
            {
                foreach (int enemy in AliveTargets(states))
                {
                    ApplyDamage(
                        owner,
                        enemies,
                        states,
                        enemy,
                        narakuHpLoss.Value,
                        ValueProp.Unblockable | ValueProp.Unpowered,
                        owner,
                        spec.Card,
                        null);
                }
            }
        }
    }

    private static void ApplyKarateWave(
        Creature owner,
        IReadOnlyList<Creature> enemies,
        ForecastState[] states,
        IReadOnlyList<(int Target, bool TriggerKarate)> primaryResults,
        ValueProp props,
        Creature dealer,
        CardModel? cardSource,
        bool triggersKarate)
    {
        int stacks = states.Length > 0 ? states[0].Karate : 0;
        int[] targets = primaryResults
            .Where(result => result.TriggerKarate
                && states[result.Target].Hp > 0
                && enemies[result.Target].Side != dealer.Side)
            .Select(result => result.Target)
            .Distinct()
            .ToArray();
        KarateWaveResolution wave = KarateWaveRules.Resolve(
            stacks,
            triggersKarate
                && props.IsPoweredAttack()
                && KarateTriggerRules.CanTriggerFromCardSource(cardSource),
            targets.Length);
        if (!wave.Triggered)
        {
            return;
        }

        foreach (int target in targets)
        {
            ApplyDamage(
                owner,
                enemies,
                states,
                target,
                wave.BonusDamagePerTarget,
                ValueProp.Unpowered,
                dealer,
                cardSource: null,
                cardPlay: null);
        }

        for (int index = 0; index < states.Length; index++)
        {
            states[index] = states[index] with { Karate = wave.RemainingStacks };
        }
    }

    private static bool ApplyDamage(
        Creature owner,
        IReadOnlyList<Creature> enemies,
        ForecastState[] states,
        int targetIndex,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource,
        CardPlay? cardPlay,
        decimal postHookMultiplier = 1m)
    {
        ForecastState state = states[targetIndex];
        if (state.Hp <= 0)
        {
            return false;
        }

        Creature target = enemies[targetIndex];
        IRunState? runState = ResolveRunState(owner);
        if (runState == null)
        {
            return false;
        }

        decimal modified = GameCompatibility.Damage.Modify(
            runState,
            owner.CombatState,
            target,
            dealer,
            amount,
            props,
            cardSource,
            cardPlay,
            ModifyDamageHookType.All,
            CardPreviewMode.None,
            out _);
        modified *= postHookMultiplier;
        int blocked = props.HasFlag(ValueProp.Unblockable)
            ? 0
            : Math.Min(state.Block, Math.Max(0, (int)modified));
        decimal hpLoss = Hook.ModifyHpLost(
            runState,
            owner.CombatState,
            target,
            Math.Max(modified - blocked, 0m),
            props,
            dealer,
            cardSource,
            HpLossHookPhase.BeforeOsty | HpLossHookPhase.AfterOsty,
            out _);
        states[targetIndex] = state with
        {
            Block = state.Block - blocked,
            Hp = state.Hp - Math.Max(0, (int)hpLoss)
        };
        return modified > 0m;
    }

    private static IEnumerable<int> AliveTargets(IReadOnlyList<ForecastState> states) =>
        Enumerable.Range(0, states.Count).Where(index => states[index].Hp > 0);

    private static IRunState? ResolveRunState(Creature owner) =>
        owner.Player?.RunState ?? owner.PetOwner?.RunState;

    // A value type: the search mutates state through `with` expressions on up to
    // FinisherForecastEngine.DefaultMaximumSearchStates nodes, and as a record class every one of
    // those was a heap allocation during targeting.
    private readonly record struct ForecastState(int Hp, int Block, int Karate, bool IsPrimaryEnemy);
    private readonly record struct ForecastStateKey(int Hp, int Block, int Karate, bool IsPrimaryEnemy);
    private readonly record struct CachedForecast(
        FinisherForecastOutcome Outcome,
        FinisherForecastResult Result);
}
