using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.ExternalAnimations;

namespace NinjaSlayer.Cards;

internal static class FinisherAttackExtensions
{
    public static bool IsFinisherMovementOwned(this Creature creature) =>
        NinjaSlayerFinisherCinematic.IsMovementOwned(creature);

    public static Task<AttackCommand> ExecuteWithFinisher(
        this AttackCommand command,
        PlayerChoiceContext choiceContext,
        CardModel card,
        CardPlay cardPlay,
        decimal? damageOverride = null,
        int? hitCountOverride = null)
    {
        return NinjaSlayerFinisherCinematic.ExecuteWithFinisher(
            command,
            choiceContext,
            card,
            cardPlay,
            damageOverride,
            hitCountOverride);
    }

    public static Task ExecuteSequenceWithFinisher(
        this CardModel card,
        PlayerChoiceContext choiceContext,
        CardPlay cardPlay,
        int hitCount,
        Func<Task> sequence)
    {
        FinisherAttackSpec spec = FinisherAttackSpec.FromCard(
            card,
            cardPlay,
            hitCountOverride: hitCount);
        return NinjaSlayerFinisherCinematic.ExecuteSequenceWithFinisher(
            choiceContext,
            spec,
            sequence);
    }

    public static Task ExecuteDirectWithFinisher(
        this CardModel card,
        PlayerChoiceContext choiceContext,
        CardPlay cardPlay,
        decimal damage,
        ValueProp props,
        Func<Task> damageAction)
    {
        var spec = new FinisherAttackSpec(
            card,
            cardPlay,
            _ => damage,
            props,
            1,
            FinisherTargeting.Single);
        return NinjaSlayerFinisherCinematic.ExecuteDirectWithFinisher(
            choiceContext,
            spec,
            damageAction);
    }
}
