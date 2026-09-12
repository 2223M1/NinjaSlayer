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
