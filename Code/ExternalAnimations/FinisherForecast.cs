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

internal interface IFinisherForecastContributor
{
    bool TryCreateEffect(Creature owner, FinisherAttackSpec spec, out FinisherForecastEffect? effect);
}

internal sealed class KusarigamaFinisherForecastContributor : IFinisherForecastContributor
{
    public bool TryCreateEffect(Creature owner, FinisherAttackSpec spec, out FinisherForecastEffect? effect)
    {
        effect = null;
        Kusarigama? kusarigama = owner.Player?.GetRelic<Kusarigama>();
        if (kusarigama == null || spec.Card.Type != CardType.Attack)
        {
            return false;
        }

        int cardsPerTrigger = kusarigama.DynamicVars.Cards.IntValue;
        if (cardsPerTrigger <= 0 || kusarigama.DisplayAmount != cardsPerTrigger - 1)
        {
            return false;
        }

        effect = new FinisherForecastEffect(
            kusarigama.DynamicVars.Damage.BaseValue,
            kusarigama.DynamicVars.Damage.Props,
            owner,
            null,
            null,
            FinisherForecastEffectTargeting.Random);
        return true;
    }
}

internal static class FinisherForecast
{
    private static readonly FrameScopedCache<FinisherForecastFrameKey, CachedForecast> FrameCache = new();
    private static readonly IFinisherForecastContributor[] PostCardContributors =
    [
        new KusarigamaFinisherForecastContributor()
    ];

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
        if (combatState == null || enemies.Any(enemy => !Hook.ShouldDie(owner.Player!.RunState, combatState, enemy, out _)))
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
        foreach (IFinisherForecastContributor contributor in PostCardContributors)
        {
            if (contributor.TryCreateEffect(owner, spec, out FinisherForecastEffect? effect) && effect != null)
            {
                forecastEffects.Add(effect);
            }
        }

        result = new FinisherForecastResult(hits, forecastEffects.Count > 0);
        ForecastState[] states = enemies.Select(enemy => new ForecastState(
            enemy.CurrentHp,
            enemy.Block,
            enemy.GetPowerAmount<KaratePower>())).ToArray();
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
            _ => throw new ArgumentOutOfRangeException(nameof(descriptor.Targeting), descriptor.Targeting, null)
        };
        if (targeting == FinisherForecastTargeting.Single && (enemies.Count != 1 || singleTargetIndex == null)
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
            state => new ForecastStateKey(state.Hp, state.Block, state.Karate),
            (current, targets, hitIndex) =>
            {
                ApplyHit(owner, enemies, current, spec, damageByTarget, narakuHpLoss, targets, hitIndex);
                return true;
            },
            singleTargetIndex,
            fixedTargets,
            postCardEffects);
        FinisherForecastOutcome outcome = FinisherForecastEngine.Evaluate(simulation);
        FrameCache.Store(frame, cacheKey, new CachedForecast(outcome, result));
        return outcome;
    }

    private static void ApplyHit(
        Creature owner,
        IReadOnlyList<Creature> enemies,
        ForecastState[] states,
        FinisherAttackSpec spec,
        IReadOnlyList<decimal> damageByTarget,
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

        foreach ((int target, bool triggerKarate) in damageResults)
        {
            ForecastState state = states[target];
            if (triggerKarate && state.Hp > 0 && state.Karate > 0 && spec.Forecast.Props.IsPoweredAttack()
                && KarateTriggerRules.CanTriggerFromCardSource(spec.Card))
            {
                ApplyDamage(
                    owner,
                    enemies,
                    states,
                    target,
                    state.Karate,
                    ValueProp.Unpowered,
                    owner,
                    null,
                    null);
                ForecastState afterKarate = states[target];
                if (afterKarate.Hp > 0)
                {
                    states[target] = afterKarate with { Karate = Math.Max(0, afterKarate.Karate - 1) };
                }
            }

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
        decimal modified = Hook.ModifyDamage(
            owner.Player!.RunState,
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
            owner.Player.RunState,
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

    private sealed record ForecastState(int Hp, int Block, int Karate);
    private readonly record struct ForecastStateKey(int Hp, int Block, int Karate);
    private readonly record struct CachedForecast(
        FinisherForecastOutcome Outcome,
        FinisherForecastResult Result);
}
