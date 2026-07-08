using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.Combat;

public static class KarateTriggerRules
{
    public static bool CanTriggerFromCardSource(CardModel? cardSource)
    {
        if (cardSource == null)
        {
            return true;
        }

        if (cardSource.Tags.Contains(NinjaSlayerCardTags.Shuriken))
        {
            return false;
        }

        if (cardSource.Tags.Contains(CardTag.Shiv))
        {
            return false;
        }

        return true;
    }

    public static bool IsMeleeAttack(CardModel card) =>
        card.Type == CardType.Attack && CanTriggerFromCardSource(card);
}
