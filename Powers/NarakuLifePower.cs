using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

[RegisterPower]
public sealed class NarakuLifePower : ModPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target, DamageResult result, MegaCrit.Sts2.Core.ValueProps.ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (target != Owner || result.UnblockedDamage <= 0 || Amount <= 0)
        {
            return;
        }

        int absorbed = Math.Min(Amount, result.UnblockedDamage);
        await CreatureCmd.Heal(Owner, absorbed, playAnim: false);
        await PowerCmd.ModifyAmount(choiceContext, this, -absorbed, Owner, cardSource, silent: true);
    }
}
