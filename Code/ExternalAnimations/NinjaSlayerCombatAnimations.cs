using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Cards;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.ExternalAnimations;

/// <summary>
/// Single routing point for NinjaSlayer combat TriggerAnim calls.
/// Visual cues select textures; procedural movement owns continuous body transforms.
/// </summary>
public static class NinjaSlayerCombatAnimations
{
    private const float DefaultBlockedHitDuration = 0.2f;

    public static bool TryPlayTriggerAnim(Creature creature, string triggerName, float waitTime, ref Task result)
    {
        if (creature.Player?.Character is not INinjaSlayerCharacter || creature.IsDead)
        {
            return false;
        }

        if (triggerName == "Attack" && FinisherRangedAction.For(creature) != null
            && NinjaSlayerAttackExecution.CurrentCommand?.ModelSource is MegaCrit.Sts2.Core.Models.Cards.Shiv)
            triggerName = "Shiv";
        var audio = NinjaSlayerCombatAudioSet.For(creature);

        switch (triggerName)
        {
            case "Shiv":
                NinjaSlayerAimPose.Get(creature)?.BeginShurikenThrow(NinjaSlayerAttackExecution.Target);
                result = Cmd.CustomScaledWait(Mathf.Min(waitTime * 0.5f, 0.25f), waitTime);
                return true;
            case "Attack":
                NinjaSlayerCombatAudioSet.Play(audio.FastAttack);
                result = PlayAttackAnimation(creature, waitTime);
                return true;
            case "SlowAttack":
                NinjaSlayerCombatAudioSet.Play(audio.SlowAttack);
                result = PlaySlowAttackAnimation(creature);
                return true;
            case "XAttack":
                if (!XAttackAudioContext.SuppressAutomaticSfx)
                {
                    NinjaSlayerCombatAudioSet.Play(audio.FastAttack);
                }

                result = PlayXAttackHit(creature, waitTime);
                return true;
            case "XAttackCue":
                result = PlayVisualCueTrigger(creature, triggerName, waitTime);
                return true;
            case TornadoFistSpinAnimation.TriggerName:
                result = NinjaSlayerAimPose.Get(creature)?.IsTornado == true
                    ? Cmd.CustomScaledWait(Mathf.Min(waitTime * 0.5f, 0.25f), waitTime)
                    : PlayVisualCueTrigger(creature, TornadoFistSpinAnimation.CueTriggerName, waitTime);
                return true;
            case "Cast":
                {
                    CardModel? currentCard = null;
                    if (CombatManager.Instance?.History is { } history)
                    {
                        var finishedPlays = history.CardPlaysFinished
                            .Where(entry => entry.Actor == creature)
                            .Select(entry => entry.CardPlay)
                            .ToHashSet();
                        currentCard = history.CardPlaysStarted
                            .Where(entry => entry.Actor == creature)
                            .LastOrDefault(entry => !finishedPlays.Contains(entry.CardPlay))
                            ?.CardPlay.Card;
                    }

                    if (currentCard is not ZazenDrink)
                    {
                        NinjaSlayerAimPose.Get(creature)?.BeginVisualMotion(NinjaSlayerAimPose.MotionKind.Cast,
                            CombatActionTimingRuntime.VisualSeconds(0.125f));
                        result = Cmd.Wait(CombatActionTimingRuntime.TriggerSeconds(waitTime));
                        return true;
                    }

                    NinjaSlayerCombatAudioSet.Play(audio.Cast);
                    result = PlayCastAnimation(creature, waitTime);
                    return true;
                }
            case "Hit":
                NinjaSlayerCombatAudioSet.Play(audio.Hurt);
                _ = PlayHitAnimation(creature);

                result = Task.CompletedTask;
                return true;
            case "BlockedHit":
                var duration = waitTime > 0f ? waitTime : DefaultBlockedHitDuration;
                _ = PlayBlockedHitAnimation(creature, duration);
                result = Task.CompletedTask;
                return true;
            default:
                return false;
        }
    }

    public static void StopSoarSpinAndReturnToIdle(Creature creature)
    {
        SoarSpinAnimation.ResetSpinVisual(creature);
        NinjaSlayerSpinMotionBlur.Get(creature)?.Reset();
        creature.GetCreatureNode()?.SetAnimationTrigger("Idle");
    }

    private static async Task PlayCastAnimation(Creature creature, float waitTime)
    {
        NinjaSlayerAimPose.Get(creature)?.BeginVisualMotion(NinjaSlayerAimPose.MotionKind.Cast,
            CombatActionTimingRuntime.VisualSeconds(0.125f));
        NinjaSlayerAimPose.Get(creature)?.BeginAirMotion(true);
        await Cmd.Wait(CombatActionTimingRuntime.TriggerSeconds(waitTime));
        SoarSpinAnimation.EnsureAirborneSpin(creature);
    }

    private static async Task PlayAttackAnimation(Creature creature, float waitTime)
    {
        if (NinjaSlayerAttackExecution.TakeDeferredRecovery())
        {
            NinjaSlayerRapidAnimationCoordinator.BeginDamageRecovery(creature);
            await Cmd.Wait(CombatActionTimingRuntime.DamageRecoverySeconds);
        }
        await FastAttackAnimation.Play(creature, waitTime);
        SoarSpinAnimation.EnsureAirborneSpin(creature);
    }

    private static async Task PlaySlowAttackAnimation(Creature creature)
    {
        if (NinjaSlayerAttackExecution.TakeDeferredRecovery())
        {
            NinjaSlayerRapidAnimationCoordinator.BeginDamageRecovery(creature);
            await Cmd.Wait(CombatActionTimingRuntime.DamageRecoverySeconds);
        }
        await SlowAttackAnimation.Play(creature);
        SoarSpinAnimation.EnsureAirborneSpin(creature);
    }

    private static async Task PlayHitAnimation(Creature creature)
    {
        await StaggerAnimation.Play(creature);
        SoarSpinAnimation.EnsureAirborneSpin(creature);
    }

    private static async Task PlayBlockedHitAnimation(Creature creature, float duration)
    {
        var motion = NinjaSlayerAimPose.Get(creature)?.BeginVisualMotion(NinjaSlayerAimPose.MotionKind.Brace,
            CombatActionTimingRuntime.VisualSeconds(duration));
        if (motion != null) await motion.Completion;
        SoarSpinAnimation.EnsureAirborneSpin(creature);
    }

    private static async Task PlayXAttackHit(Creature creature, float waitTime)
    {
        await PlayVisualCueTrigger(creature, "XAttackCue", waitTime);
    }

    private static async Task PlayVisualCueTrigger(Creature creature, string triggerName, float waitTime)
    {
        var creatureNode = creature.GetCreatureNode();
        if (creatureNode == null)
        {
            return;
        }

        if (NinjaSlayerAimPose.Get(creature)?.OwnsSpin != true)
        {
            creatureNode.SetAnimationTrigger(triggerName);
            float duration = triggerName == TornadoFistSpinAnimation.CueTriggerName
                ? TornadoFistSpinAnimation.TurnSeconds : .24f;
            _ = SoarSpinAnimation.PlayCueSpin(creature, duration);
        }
        await Cmd.CustomScaledWait(Mathf.Min(waitTime * 0.5f, 0.25f), waitTime);
        SoarSpinAnimation.EnsureAirborneSpin(creature);
    }
}
