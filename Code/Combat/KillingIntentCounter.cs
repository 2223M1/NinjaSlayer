using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Lifecycle;

namespace NinjaSlayer.Code.Combat;

internal static class KillingIntentCounter
{
    internal static async Task Execute(
        PlayerChoiceContext choiceContext, Creature owner, IReadOnlyList<Creature> targets, decimal damage)
    {
        var combat = owner.CombatState!;
        // A damage source only: it never enters a pile or fires card-play/generated-card hooks.
        var source = (StraightKi)ModelDb.Card<StraightKi>().ToMutable();
        source.Owner = owner.Player!;
        source.DynamicVars.Damage.BaseValue = damage;
        AttackCommand command = DamageCmd.Attack(damage)
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
            .FromCard(source)
#else
            .FromCard(source, null)
#endif
            .TargetingAllOpponents(combat);
        await Hook.BeforeAttack(combat, command);
        var presentation = RapidCardPresentationContext.Begin(source);
        var execution = NinjaSlayerAttackExecution.Enter(command, targets[0]);
        try
        {
            await SlowAttackAnimation.PlaySomersault(owner);
            if (!owner.IsAlive || CombatManager.Instance.IsOverOrEnding) return;
            Creature[] livingTargets = targets.Where(target => target.IsAlive && target.IsHittable
                && ReferenceEquals(target.CombatState, combat)).ToArray();
            if (livingTargets.Length == 0) return;
            VfxCmd.PlayOnCreatures(livingTargets, VfxCmd.heavyBluntPath);
            NDebugAudioManager.Instance?.Play(TmpSfx.heavyAttack);
            List<DamageResult> results = (await CreatureCmd.Damage(
                choiceContext, livingTargets, damage, ValueProp.Move, owner, source
#if !NINJASLAYER_LEGACY_DAMAGE_API
                , null
#endif
            )).ToList();
            command.AddResultsInternal(results);
            CombatManager.Instance.History.CreatureAttacked(combat, owner, results);
        }
        finally
        {
            try { await Hook.AfterAttack(combat, choiceContext, command); }
            finally
            {
                NinjaSlayerRapidAnimationCoordinator.CardGameplaySettled(owner);
                execution.RestoreCaller();
                presentation.RestoreCallerContext();
            }
        }
    }
}
