using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.Combat;

internal static class VanillaHitPreviewCompatibility
{
    public const string SupportedGameVersion = "0.109.x";

    private static readonly Dictionary<Type, int> HitCounts = new()
    {
        [typeof(TwinStrike)] = 2,
        [typeof(Thrash)] = 2,
        [typeof(RipAndTear)] = 2,
        [typeof(Uproar)] = 2,
        [typeof(Refract)] = 2,
        [typeof(AstralPulse)] = 2,
        [typeof(Maul)] = 2,
        [typeof(DaggerSpray)] = 2,
    };

    public static bool TryGetHitCount(CardModel card, Creature? target, out int hitCount)
    {
        if (card.GetType().Assembly != typeof(CardModel).Assembly || card.Type != CardType.Attack)
        {
            hitCount = 0;
            return false;
        }

        if (card is Spite)
        {
            hitCount = NinjaSlayerCombatMetrics.LostHpThisTurn(card.Owner.Creature)
                ? card.DynamicVars.Repeat.IntValue
                : 1;
            return true;
        }

        if (card.EnergyCost.CostsX)
        {
            int xValue = ResolvePreviewXValue(card);
            hitCount = card switch
            {
                HeavenlyDrill when xValue >= card.DynamicVars.Energy.IntValue => xValue * 2,
                Volley => 0,
                _ => xValue
            };
            return true;
        }

        hitCount = HitCounts.GetValueOrDefault(card.GetType(), 1);
        return true;
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
}
