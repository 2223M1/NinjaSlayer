using System.Diagnostics.CodeAnalysis;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Code.Lifecycle;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class FinisherAttackCommandAdapter
{
    public static bool TryCreateSpec(
        AttackCommand command,
        [NotNullWhen(true)] out FinisherAttackSpec? spec)
    {
        spec = null;
        if (!GameCompatibility.Finisher.TryReadAttackCommand(
                command,
                out GameCompatibility.AttackCommandState commandState)
            || command.ModelSource is not CardModel { Type: CardType.Attack } card
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

        CalculatedDamageVar? calculatedDamage = commandState.CalculatedDamage;
        decimal damagePerHit = commandState.DamagePerHit;
        int hitCount = commandState.HitCount;
        Creature? singleTarget = commandState.SingleTarget;
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
