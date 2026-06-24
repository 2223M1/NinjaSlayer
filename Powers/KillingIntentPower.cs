using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

[RegisterPower]
public sealed class KillingIntentPower : ModPowerTemplate
{
    private decimal pendingDamage;
    private decimal reflectedDamage;
    private Creature? reflectedDealer;

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public override decimal ModifyDamageAdditive(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (target == Owner && props.IsPoweredAttack() && dealer != null)
        {
            pendingDamage = 0;
            reflectedDamage = 0;
            reflectedDealer = null;
        }

        if (ShouldReduceBlockedAttack(target, props, dealer))
        {
            pendingDamage = amount;
        }

        return 0;
    }

    public override decimal ModifyDamageCap(Creature? target, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        ICombatState? combatState = Owner.CombatState;
        if (combatState == null || !ShouldReduceBlockedAttack(target, props, dealer) || pendingDamage <= 1)
        {
            return decimal.MaxValue;
        }

        decimal uncappedDamage = Hook.ModifyDamage(
            combatState.RunState,
            combatState,
            target,
            dealer,
            pendingDamage,
            props,
            cardSource,
            ModifyDamageHookType.Additive | ModifyDamageHookType.Multiplicative,
            CardPreviewMode.None,
            out _
        );
        if (uncappedDamage > 1)
        {
            reflectedDamage = Math.Min(uncappedDamage, Owner.Block);
            reflectedDealer = dealer;
        }

        return 1;
    }

    public override async Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (target != Owner || result.BlockedDamage <= 0 || reflectedDamage <= 0 || reflectedDealer != dealer || !props.IsPoweredAttack() || dealer == null)
        {
            ClearReflection();
            return;
        }

        Flash();
        decimal damage = reflectedDamage;
        ClearReflection();
        IEnumerable<DamageResult> results = await CreatureCmd.Damage(choiceContext, dealer, damage, ValueProp.Unpowered, Owner, null);
        if (results.Any(r => r.Receiver == dealer && r.UnblockedDamage > 0))
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

    private bool ShouldReduceBlockedAttack(Creature? target, ValueProp props, Creature? dealer)
    {
        return target == Owner && Owner.Block > 0 && props.IsPoweredAttack() && !props.HasFlag(ValueProp.Unblockable) && dealer != null;
    }

    private void ClearReflection()
    {
        pendingDamage = 0;
        reflectedDamage = 0;
        reflectedDealer = null;
    }
}
