using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class WasssssshoiPower : RedesignV1CounterPower
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named("DamageFocusPower");
    public override Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer,
        DamageResult result, ValueProp props, Creature target, CardModel? cardSource) =>
        dealer == Owner && target.Side != Owner.Side && result.TotalDamage > 0
        && cardSource?.Type == CardType.Attack
        && props.IsPoweredAttack()
            ? GainTemporaryStats(choiceContext, cardSource) : Task.CompletedTask;

    internal async Task GainTemporaryStats(PlayerChoiceContext choiceContext, CardModel? source)
    {
        await PowerCmd.Apply<WasssssshoiStrengthPower>(choiceContext, Owner, Amount, Owner, source);
        await PowerCmd.Apply<WasssssshoiFocusPower>(choiceContext, Owner, Amount, Owner, source);
    }
}
