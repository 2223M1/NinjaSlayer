using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Powers;

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
            && KarateCombatPreviewContext.TryGetCard(target) == previewCard
            && KarateTriggerRules.CanTriggerFromCardSource(previewCard);
        int hits = isPreviewTarget ? ResolveHitCount(previewCard!, target) : 1;
        int multiplier = previewCard is ClankDrinkTeaRedesignV1 assassination
            ? assassination.KarateMultiplier
            : 1;
        return KarateDamageMath.ForecastDamage(stacks, hits, isPreviewTarget) * multiplier;
    }

    public static int ResolveHpPreviewDamage(int stacks, CardModel? previewCard, Creature target)
    {
        if (stacks <= 0)
        {
            return 0;
        }

        bool isPreviewTarget = previewCard?.Type == CardType.Attack
            && KarateCombatPreviewContext.TryGetCard(target) == previewCard
            && KarateTriggerRules.CanTriggerFromCardSource(previewCard);
        int hits = isPreviewTarget ? ResolveHitCount(previewCard!, target) : 1;
        int multiplier = previewCard is ClankDrinkTeaRedesignV1 assassination
            ? assassination.KarateMultiplier
            : 1;
        return KarateDamageMath.HpPreviewDamage(stacks, hits, isPreviewTarget) * multiplier;
    }

    public static int CumulativeDamage(int stack, int hits)
        => KarateDamageMath.CumulativeDamage(stack, hits);

    public static int RemainingKarateAfterTriggers(Creature? target, CardModel _)
    {
        int karate = target?.GetPowerAmount<KaratePower>() ?? 0;
        return Math.Max(0, karate);
    }

    public static int ResolveHitCount(CardModel card, Creature? target)
    {
        return HitPreviewResolver.TryResolve(card, target, out int hitCount)
            ? hitCount
            : 1;
    }
}
