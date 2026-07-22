using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Code.Combat;

public static class KarateForecastCalculator
{
    public static int ResolveForecastDamage(KaratePower karate, CardModel? previewCard, Creature target)
    {
        int stack = karate.Amount;
        if (stack <= 0)
        {
            return 0;
        }

        if (previewCard == null || previewCard.Type != CardType.Attack || KarateCombatPreviewContext.TryGetCard(target) != previewCard)
        {
            return stack;
        }

        if (!KarateTriggerRules.CanTriggerFromCardSource(previewCard))
        {
            return stack;
        }

        return KarateDamageMath.CumulativeDamage(stack, ResolveHitCount(previewCard, target));
    }

    public static int ResolveHpPreviewDamage(KaratePower karate, CardModel? previewCard, Creature target)
    {
        int stack = karate.Amount;
        if (stack <= 0)
        {
            return 0;
        }

        if (previewCard == null || previewCard.Type != CardType.Attack || KarateCombatPreviewContext.TryGetCard(target) != previewCard)
        {
            return 0;
        }

        if (!KarateTriggerRules.CanTriggerFromCardSource(previewCard))
        {
            return 0;
        }

        return KarateDamageMath.CumulativeDamage(stack, ResolveHitCount(previewCard, target));
    }

    public static int CumulativeDamage(int stack, int hits)
        => KarateDamageMath.CumulativeDamage(stack, hits);

    public static int RemainingKarateAfterTriggers(Creature? target, CardModel card)
    {
        int karate = target?.GetPowerAmount<KaratePower>() ?? 0;
        if (karate <= 0)
        {
            return 0;
        }

        int hits = ResolveHitCount(card, target);
        return Math.Max(0, karate - Math.Min(karate, hits));
    }

    public static int ResolveHitCount(CardModel card, Creature? target)
    {
        return HitPreviewResolver.TryResolve(card, target, out int hitCount)
            ? hitCount
            : 1;
    }
}
