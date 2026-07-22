using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards;
using NinjaSlayer.Content;
using STS2RitsuLib.Cards.DynamicVars;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class KillingIntentPower : NinjaSlayerPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new PowerVar<WeakPower>(3),
        new PowerVar<VulnerablePower>(3)
    ];

    public override Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        if (cardSource is KillingIntent killingIntent)
        {
            DynamicVars.Weak.BaseValue = killingIntent.DynamicVars.Weak.BaseValue;
            DynamicVars.Vulnerable.BaseValue = killingIntent.DynamicVars.Vulnerable.BaseValue;
        }

        return Task.CompletedTask;
    }

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

        await CreatureCmd.Damage(choiceContext, dealer, result.BlockedDamage, ValueProp.Unpowered, Owner);
        await CombatManager.Instance.CheckWinCondition();

    }

    public override async Task AfterAttack(PlayerChoiceContext choiceContext, AttackCommand command)
    {
        if (command.Attacker is not { } attacker
            || !CombatManager.Instance.IsInProgress
            || !attacker.IsAlive
            || !command.DamageProps.IsPoweredAttack())
        {
            return;
        }

        List<DamageResult> results = command.Results.SelectMany(r => r).ToList();
        if (!results.Any(r => r.Receiver == Owner && r.WasFullyBlocked && r.BlockedDamage > 0))
        {
            return;
        }

        int weakAmount = DynamicVars.Weak.IntValue;
        int vulnerableAmount = DynamicVars.Vulnerable.IntValue;
        if (weakAmount > 0)
        {
            await PowerCmd.Apply<WeakPower>(choiceContext, attacker, weakAmount, Owner, null);
        }

        if (vulnerableAmount > 0)
        {
            await PowerCmd.Apply<VulnerablePower>(choiceContext, attacker, vulnerableAmount, Owner, null);
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
