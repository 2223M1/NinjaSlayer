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
using NinjaSlayer.Code.ExternalAnimations;
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
        if (target != Owner || dealer == null || !props.IsPoweredAttack() || !CanCounter)
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
        if (!advance.ShouldCounter || !CanCounter)
        {
            return;
        }

        await TryCounter(choiceContext, dealer);
    }

    private bool CanCounter => Owner.IsAlive
        && Owner.CombatState is { } combat && combat.IsLiveCombat() && combat.ContainsCreature(Owner)
        && FinisherSessionRegistry.GetActiveSession()?.HasConfirmedDeath(Owner) != true;

    private async Task TryCounter(PlayerChoiceContext choiceContext, Creature target)
    {
        await StaggerAnimation.WaitForCompletion(Owner);
        ICombatState? combatState = Owner.CombatState;
        if (!CanCounter
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
        FinisherApproach? approach = null;
        try
        {
            if (!CanCounter) return;
            Flash();
            if (FinisherAttackCommandAdapter.PredictReverseVictim(command, [target], 0m, 1)
                ?.GetCreatureNode() is { } focus && Owner.GetCreatureNode() is { } actor)
            {
                approach = FinisherApproach.Create(actor, focus, Godot.Vector2.One);
                approach.ReturnDuration = SlowAttackAnimation.IaiReturnSeconds;
                approach.Start(SlowAttackAnimation.IaiPeakSeconds);
            }
            await CreatureCmd.TriggerAnim(
                Owner,
                "SlowAttack",
                SlowAttackAnimation.IaiNormalSeconds);
            if (!CanCounter || !target.IsAlive || !target.IsHittable
                || !combatState.IsLiveCombat()) return;
            if (willConnect)
            {
                NinjaSlayerCombatVfx.PlayDarkCounterHitFx(target);
            }

            FinisherApproach.ReachImpact(Owner);
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
            approach?.ReleasePrediction();
            await Hook.AfterAttack(combatState, choiceContext, command);
        }
    }
}
