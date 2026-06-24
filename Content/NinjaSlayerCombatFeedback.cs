using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Models;

namespace NinjaSlayer.Content;

[RegisterSingleton]
public sealed class NinjaSlayerCombatFeedback : HookedSingletonModel
{
    public NinjaSlayerCombatFeedback() : base(HookType.Combat)
    {
    }

    public override async Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (target.Player?.Character is not NinjaSlayerCharacter || !props.IsPoweredAttack())
        {
            return;
        }

        if (result.UnblockedDamage > 0)
        {
            return;
        }
        else if (result.BlockedDamage > 0)
        {
            await CreatureCmd.TriggerAnim(target, "BlockedHit", 0.2f);
        }
    }
}
