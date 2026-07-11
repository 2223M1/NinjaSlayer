using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Hooks;
using NinjaSlayer.Cards;
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

        return CumulativeDamage(stack, ResolveHitCount(previewCard, target));
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

        return CumulativeDamage(stack, ResolveHitCount(previewCard, target));
    }

    public static int CumulativeDamage(int stack, int hits)
    {
        if (stack <= 0 || hits <= 0)
        {
            return 0;
        }

        int triggers = Math.Min(stack, hits);
        return triggers * (2 * stack - triggers + 1) / 2;
    }

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
        if (card is NinjaSlayerXAttackCard xAttackCard)
        {
            return Math.Max(0, xAttackCard.GetPreviewHitCount());
        }

        if (card.DynamicVars.TryGetValue("CalculatedHits", out DynamicVar? calculatedHitsVar) && calculatedHitsVar != null)
        {
            card.UpdateDynamicVarPreview(CardPreviewMode.Normal, target, card.DynamicVars);
            return Math.Max(0, (int)calculatedHitsVar.PreviewValue);
        }

        if (card.Type == CardType.Attack && card is Spite)
        {
            return LostHpThisTurn(card.Owner.Creature) ? card.DynamicVars.Repeat.IntValue : 1;
        }

        if (card.Type == CardType.Attack && card.DynamicVars.TryGetValue("Repeat", out DynamicVar? repeatVar) && repeatVar != null)
        {
            return Math.Max(0, repeatVar.IntValue);
        }

        if (card.Type == CardType.Attack && card.EnergyCost.CostsX)
        {
            int xValue = ResolvePreviewXValue(card);
            if (card is HeavenlyDrill && xValue >= card.DynamicVars.Energy.IntValue)
            {
                return xValue * 2;
            }

            if (card is Volley)
            {
                return 0;
            }

            return xValue;
        }

        if (KarateKnownMultiHitCards.TryGetHitCount(card, out int knownHits))
        {
            return knownHits;
        }

        return 1;
    }

    private static int ResolvePreviewXValue(CardModel card)
    {
        int xValue = card.EnergyCost.GetAmountToSpend();
        if (card.Pile != null && card.CombatState is { } combatState)
        {
            xValue = Hook.ModifyXValue(combatState, card, xValue);
        }

        return Math.Max(0, xValue);
    }

    private static bool LostHpThisTurn(Creature creature)
    {
        return CombatManager.Instance.History.Entries
            .OfType<DamageReceivedEntry>()
            .Any(e => e.HappenedThisTurn(creature.CombatState)
                && e.Receiver == creature
                && e.Result.UnblockedDamage > 0);
    }
}
