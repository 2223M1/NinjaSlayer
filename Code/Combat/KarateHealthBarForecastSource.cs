using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using STS2RitsuLib.Combat.HealthBars;

namespace NinjaSlayer.Code.Combat;

public sealed class KarateHealthBarForecastSource : IHealthBarForecastSource
{
    public IEnumerable<HealthBarForecastSegment> GetHealthBarForecastSegments(HealthBarForecastContext context)
    {
        Creature target = context.Creature;
        if (target.IsDead || context.CombatState == null)
        {
            return [];
        }

        CardModel? previewCard = KarateCombatPreviewContext.CurrentCard;
        Creature? attacker = previewCard?.Owner.Creature
            ?? context.CombatState.Players.FirstOrDefault(LocalContext.IsMe)?.Creature;
        if (attacker == null || attacker.IsDead || attacker.Side == target.Side)
        {
            return [];
        }

        int stacks = attacker.GetPowerAmount<KaratePower>();
        int damage = KarateForecastCalculator.ResolveForecastDamage(stacks, previewCard, target);
        if (damage <= 0)
        {
            return [];
        }

        return HealthBarForecasts
            .FromRight(context, KarateHealthBarColors.Middleground, KarateHealthBarColors.Middleground)
            .Add(damage)
            .Build();
    }
}
