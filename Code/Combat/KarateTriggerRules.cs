using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.Combat;

public static class KarateTriggerRules
{
    public static bool CanTriggerFromCardSource(CardModel? card) =>
        card?.GetType().Name != "AlabamaDropRedesignV1";

    public static bool IsMeleeAttack(CardModel card) =>
        card.Type == CardType.Attack
        && !card.Tags.Contains(NinjaSlayerCardTags.Shuriken)
        && !card.Tags.Contains(CardTag.Shiv);
}
