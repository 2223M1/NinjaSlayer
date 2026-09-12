using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;

namespace NinjaSlayer.Code.Combat;

public static class KarateForecastCalculator
{
    public static int ResolveForecastDamage(int stacks, CardModel? previewCard, Creature target)
    {
        if (stacks <= 0)
        {
            return 0;
        }

        bool isPreviewTarget = previewCard?.Type == CardType.Attack
            && KarateCombatPreviewContext.TryGetCard(target) == previewCard;
        int hits = isPreviewTarget ? ResolveHitCount(previewCard!, target) : 1;
        return KarateDamageMath.ForecastDamage(stacks, hits, isPreviewTarget);
    }

    public static int ResolveHpPreviewDamage(int stacks, CardModel? previewCard, Creature target)
    {
        if (stacks <= 0)
        {
            return 0;
        }

        bool isPreviewTarget = previewCard?.Type == CardType.Attack
            && KarateCombatPreviewContext.TryGetCard(target) == previewCard;
        int hits = isPreviewTarget ? ResolveHitCount(previewCard!, target) : 1;
        return KarateDamageMath.HpPreviewDamage(stacks, hits, isPreviewTarget);
    }

    public static int ResolveHitCount(CardModel card, Creature? target)
    {
        return VanillaHitPreviewCompatibility.TryGetHitCount(card, target, out int hitCount)
            ? hitCount
            : 1;
    }
}
