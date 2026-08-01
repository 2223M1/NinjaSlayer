#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Code.Compatibility;

namespace MegaCrit.Sts2.Core.Commands;

internal static class LegacyAttackCommandExtensions
{
    public static AttackCommand FromCard(
        this AttackCommand command,
        CardModel card,
        CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(cardPlay);
        GameCompatibility.CardPlays.AssociatePlayer(cardPlay, card.Owner);
        GameCompatibility.AttackCommands.AssociateCardPlay(command, cardPlay);
        return command.FromCard(card);
    }
}
#endif
