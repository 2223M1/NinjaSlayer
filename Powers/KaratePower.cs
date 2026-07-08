using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Content;
using STS2RitsuLib.Combat.HealthBars;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

[RegisterPower]
public sealed class KaratePower : ModPowerTemplate, IHealthBarForecastSource
{
    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.For(GetType());

    public IEnumerable<HealthBarForecastSegment> GetHealthBarForecastSegments(HealthBarForecastContext context)
    {
        if (Amount <= 0 || context.Creature != Owner)
        {
            return [];
        }

        CardModel? previewCard = KarateCombatPreviewContext.TryGetCard(context.Creature);
        int damage = KarateForecastCalculator.ResolveForecastDamage(this, previewCard, context.Creature);
        if (damage <= 0)
        {
            return [];
        }

        return HealthBarForecasts.FromRight(context, KarateHealthBarColors.Middleground, KarateHealthBarColors.Middleground)
            .Add(damage)
            .Build();
    }

    public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer, DamageResult result, ValueProp props, Creature target, CardModel? cardSource)
    {
        if (target == Owner
            && props.IsPoweredAttack()
            && result.TotalDamage > 0
            && KarateTriggerRules.CanTriggerFromCardSource(cardSource))
        {
            await Content.NinjaSlayerActions.TriggerKarate(choiceContext, dealer, target, cardSource);
        }
    }
}
