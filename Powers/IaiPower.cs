using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Content;

namespace NinjaSlayer.Powers;

public sealed class IaiPower : NinjaSlayerPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterDamageReceived(
        PlayerChoiceContext choiceContext,
        Creature target,
        DamageResult result,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource)
    {
        if (target != Owner || dealer == null || !props.IsPoweredAttack())
        {
            return;
        }

        DarkCounterAdvance advance = DarkNinjaCombatMath.AdvanceCounter(Amount);
        await PowerCmd.ModifyAmount(
            choiceContext,
            this,
            advance.RemainingHits - Amount,
            Owner,
            cardSource);
        if (!advance.ShouldCounter)
        {
            return;
        }

        Flash();
        await TryCounter(choiceContext, dealer);
    }

    private async Task TryCounter(PlayerChoiceContext choiceContext, Creature target)
    {
        ICombatState? combatState = Owner.CombatState;
        if (!Owner.IsAlive
            || combatState == null
            || !combatState.IsLiveCombat()
            || !target.IsAlive
            || !target.IsHittable
            || target.Side == Owner.Side
            || !ReferenceEquals(target.CombatState, combatState))
        {
            return;
        }

        decimal damage = Hook.ModifyDamage(
            combatState.RunState,
            combatState,
            target,
            Owner,
            0m,
            ValueProp.Move,
            null,
#if !NINJASLAYER_LEGACY_DAMAGE_API
            null,
#endif
            ModifyDamageHookType.All,
            CardPreviewMode.Normal,
            out _);
        if (damage <= 0m)
        {
            return;
        }

        bool willConnect = target.GetPower<EvasionPower>() is not { } evasion
            || !evasion.CanEvade(target, ValueProp.Move, Owner);
        AttackCommand command = DamageCmd.Attack(0m).FromMonster(Owner.Monster!);
        await Hook.BeforeAttack(combatState, command);
        try
        {
            await CreatureCmd.TriggerAnim(
                Owner,
                "SlowAttack",
                CombatActionTiming.SlowAttackNormalSeconds);
            if (willConnect)
            {
                NinjaSlayerCombatVfx.PlayDefectStrikeHitFx(target);
            }

            List<DamageResult> results = (await CreatureCmd.Damage(
                choiceContext,
                [target],
                0m,
                ValueProp.Move,
                Owner,
                null
#if !NINJASLAYER_LEGACY_DAMAGE_API
                , null
#endif
            )).ToList();
            command.AddResultsInternal(results);
            CombatManager.Instance.History.CreatureAttacked(combatState, Owner, results);
        }
        finally
        {
            await Hook.AfterAttack(combatState, choiceContext, command);
        }
    }
}
