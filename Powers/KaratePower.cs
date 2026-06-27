using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Interop.AutoRegistration;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

[RegisterPower]
public sealed class KaratePower : ModPowerTemplate
{
    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.For(GetType());

    public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer, DamageResult result, ValueProp props, Creature target, CardModel? cardSource)
    {
        if (target == Owner && props.IsPoweredAttack() && result.TotalDamage > 0)
        {
            await Content.NinjaSlayerActions.TriggerKarate(choiceContext, dealer, target, cardSource);
        }

        if (dealer == Owner && target.Player == null && props.IsPoweredAttack() && result.TotalDamage > 0)
        {
            await PowerCmd.Remove<KaratePower>(Owner);
        }
    }
}
