using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Code.ExternalAnimations;

namespace NinjaSlayer.Cards;

public static class FinisherAttackExtensions
{
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
}
