using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Code.Lifecycle;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class FinisherAttackCommandAdapter
{
    private static readonly FieldInfo DamagePerHit =
        AccessTools.Field(typeof(AttackCommand), "_damagePerHit")
        ?? throw new MissingFieldException(typeof(AttackCommand).FullName, "_damagePerHit");
    private static readonly FieldInfo CalculatedDamage =
        AccessTools.Field(typeof(AttackCommand), "_calculatedDamageVar")
        ?? throw new MissingFieldException(typeof(AttackCommand).FullName, "_calculatedDamageVar");
    private static readonly FieldInfo HitCount =
        AccessTools.Field(typeof(AttackCommand), "_hitCount")
        ?? throw new MissingFieldException(typeof(AttackCommand).FullName, "_hitCount");
    private static readonly FieldInfo SingleTarget =
        AccessTools.Field(typeof(AttackCommand), "_singleTarget")
        ?? throw new MissingFieldException(typeof(AttackCommand).FullName, "_singleTarget");
    private static readonly FieldInfo AfterAnimation = AccessTools.Field(typeof(AttackCommand), "_afterAttackerAnim")!;
    private static readonly FieldInfo BeforeDamage = AccessTools.Field(typeof(AttackCommand), "_beforeDamage")!;
    private static readonly FieldInfo WaitBeforeHit = AccessTools.Field(typeof(AttackCommand), "_waitBeforeHit")!;

    internal static float AdditionalHitWait(AttackCommand command)
    {
        float[] waits = (float[])WaitBeforeHit.GetValue(command)!;
        return Math.Max(0f, NinjaSlayer.Code.Combat.CombatActionTimingRuntime.Resolve(waits[1], waits[0]));
    }

    internal static Creature? PredictReverseVictim(AttackCommand command, int hits, out IReadOnlyList<Creature> targets)
    {
        targets = [];
        if (command.IsRandomlyTargeted || CalculatedDamage.GetValue(command) != null
            || AfterAnimation.GetValue(command) != null || BeforeDamage.GetValue(command) != null
            || command.Attacker is not { IsMonster: true } actor || actor.CombatState == null) return null;
        Creature? single = (Creature?)SingleTarget.GetValue(command);
        targets = single != null ? [single]
            : command.IsMultiTargeted ? actor.CombatState.PlayerCreatures.ToArray() : [];
        return PredictReverseVictim(command, targets, (decimal)DamagePerHit.GetValue(command)!, hits);
    }

    internal static Creature? PredictReverseVictim(AttackCommand command, IEnumerable<Creature> targets,
        decimal damage, int hits)
    {
        if (command.Attacker is not { IsAlive: true } actor || actor.CombatState == null || hits <= 0) return null;
        foreach (Creature target in targets)
        {
            if (target.Player?.Character is not INinjaSlayerCharacter || !target.IsAlive || !target.IsHittable
                || target.Side == actor.Side || target.GetPower<EvasionPower>() is { } evasion
                    && evasion.CanEvade(target, command.DamageProps, actor)) continue;
            decimal perHit = Hook.ModifyDamage(target.Player.RunState, actor.CombatState, target, actor,
                damage, command.DamageProps, null,
#if !NINJASLAYER_LEGACY_DAMAGE_API
                null,
#endif
                ModifyDamageHookType.All, CardPreviewMode.None, out _);
            decimal remaining = Math.Max(0m, decimal.Floor(perHit)) * hits
                - (command.DamageProps.HasFlag(ValueProp.Unblockable) ? 0 : target.Block);
            if (remaining >= target.CurrentHp) return target;
        }
        return null;
    }

    public static bool TryCreateSpec(
        AttackCommand command,
        [NotNullWhen(true)] out FinisherAttackSpec? spec)
    {
        spec = null;
        if (command.ModelSource is not CardModel { Type: CardType.Attack } card
            || command.Attacker == null
            || card.Owner?.Creature != command.Attacker)
        {
            return false;
        }

#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
        if (!CardPlayResolutionScope.TryResolveCurrentPlay(card, out CardPlay? cardPlay))
#else
        CardPlay? cardPlay = command.CardPlay;
        if (cardPlay == null)
#endif
        {
            return false;
        }

        decimal damagePerHit = (decimal)DamagePerHit.GetValue(command)!;
        var calculatedDamage = (CalculatedDamageVar?)CalculatedDamage.GetValue(command);
        int hitCount = (int)HitCount.GetValue(command)!;
        var singleTarget = (Creature?)SingleTarget.GetValue(command);
        FinisherTargeting? targeting = command.IsRandomlyTargeted
            ? FinisherTargeting.Random
            : command.IsSingleTargeted
                ? FinisherTargeting.Single
                : command.IsMultiTargeted
                    ? FinisherTargeting.All
                    : null;
        if (targeting == null || targeting == FinisherTargeting.Single && singleTarget == null)
        {
            return false;
        }

        Func<Creature, decimal> damage = calculatedDamage switch
        {
            null => _ => damagePerHit,
            _ when command.IsMultiTargeted && !command.IsRandomlyTargeted => _ => calculatedDamage.Calculate(null),
            _ => target => calculatedDamage.Calculate(target)
        };
        spec = new FinisherAttackSpec(
            card,
            cardPlay,
            new FinisherForecastDescriptor(
                damage,
                command.DamageProps,
                Math.Max(1, hitCount),
                targeting.Value,
                singleTarget));
        return true;
    }
}
