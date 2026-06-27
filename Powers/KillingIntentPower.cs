using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Interop.AutoRegistration;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

[RegisterPower]
public sealed class KillingIntentPower : ModPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.For(GetType());

    public override async Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (target != Owner || !result.WasFullyBlocked || result.BlockedDamage <= 0 || !props.IsPoweredAttack() || dealer == null)
        {
            return;
        }

        Flash();

        if (result.BlockedDamage > 1)
        {
            await CreatureCmd.GainBlock(Owner, result.BlockedDamage - 1, ValueProp.Unpowered, null, fast: true);
        }

        IEnumerable<DamageResult> reflectResults = await CreatureCmd.Damage(
            choiceContext, dealer, result.BlockedDamage, ValueProp.Unpowered, Owner, null);
        await CombatManager.Instance.CheckWinCondition();

        if (CombatManager.Instance.IsInProgress && dealer.IsAlive
            && reflectResults.Any(r => r.Receiver == dealer && r.UnblockedDamage > 0))
        {
            await PowerCmd.Apply<WeakPower>(choiceContext, dealer, 1, Owner, cardSource);
            await PowerCmd.Apply<VulnerablePower>(choiceContext, dealer, 1, Owner, cardSource);
        }
    }

    public override async Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        if (participants.Contains(Owner))
        {
            await PowerCmd.Decrement(this);
        }
    }
}
