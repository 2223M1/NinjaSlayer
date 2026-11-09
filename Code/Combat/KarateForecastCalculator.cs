using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Code.Combat;

public static class KarateForecastCalculator
{
    public static int ResolveForecastDamage(Creature attacker, CardModel? previewCard, Creature target)
    {
        bool isPreviewTarget = previewCard?.Type == CardType.Attack
            && KarateCombatPreviewContext.TryGetCard(target) == previewCard;
        int hits = isPreviewTarget ? ResolveHitCount(previewCard!, target) : 1;
        return KarateDamageMath.ForecastDamage(attacker.GetPowerAmount<KaratePower>(), hits, isPreviewTarget,
            attacker.GetPowerAmount<NarakuLifePower>(), attacker.GetPowerAmount<OneMindOneBodyPower>());
    }

    public static int ResolveHpPreviewDamage(Creature attacker, CardModel? previewCard, Creature target)
    {
        bool isPreviewTarget = previewCard?.Type == CardType.Attack
            && KarateCombatPreviewContext.TryGetCard(target) == previewCard;
        int hits = isPreviewTarget ? ResolveHitCount(previewCard!, target) : 1;
        return KarateDamageMath.HpPreviewDamage(attacker.GetPowerAmount<KaratePower>(), hits, isPreviewTarget,
            attacker.GetPowerAmount<NarakuLifePower>(), attacker.GetPowerAmount<OneMindOneBodyPower>());
    }

    public static int ResolveHitCount(CardModel card, Creature? target)
    {
        return VanillaHitPreviewCompatibility.TryGetHitCount(card, target, out int hitCount)
            ? hitCount
            : 1;
    }
}
