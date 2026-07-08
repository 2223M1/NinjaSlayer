using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.ExternalAnimations;

/// <summary>
/// Single routing point for NinjaSlayer combat TriggerAnim calls.
/// Attack/Hit/Cast/BlockedHit play ExternalAnimations; XAttackCue uses VisualCue state machine only.
/// </summary>
public static class NinjaSlayerCombatAnimations
{
    private const float DefaultBlockedHitDuration = 0.2f;

    public static bool TryPlayTriggerAnim(Creature creature, string triggerName, float waitTime, ref Task result)
    {
        if (creature.Player?.Character is not NinjaSlayerCharacter || creature.IsDead)
        {
            return false;
        }

        var audio = NinjaSlayerCombatAudioSet.For(creature);

        switch (triggerName)
        {
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
            case "Cast":
                if (NinjaSlayerCombatCastContext.GetCurrentCard(creature) is not IDrawCastSkillCard)
                {
                    result = Task.CompletedTask;
                    return true;
                }

                NinjaSlayerCombatAudioSet.Play(audio.Cast);
                result = PlayCastAnimation(creature);
                return true;
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

    private static async Task PlayCastAnimation(Creature creature)
    {
        await HopAnimation.Play(creature);
        SoarSpinAnimation.EnsureAirborneSpin(creature);
    }

    private static async Task PlayAttackAnimation(Creature creature, float waitTime)
    {
        await FastAttackAnimation.Play(creature, waitTime);
        SoarSpinAnimation.EnsureAirborneSpin(creature);
    }

    private static async Task PlaySlowAttackAnimation(Creature creature)
    {
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
        await ShakeAnimation.Play(creature, duration, duration);
        SoarSpinAnimation.EnsureAirborneSpin(creature);
    }

    private static async Task PlayXAttackHit(Creature creature, float waitTime)
    {
        if (XAttackComboContext.Active)
        {
            await Task.WhenAll(
                PlayVisualCueTrigger(creature, "XAttackCue", waitTime),
                XAttackComboMovement.PlayHitMovement(creature, waitTime));
            return;
        }

        await PlayVisualCueTrigger(creature, "XAttackCue", waitTime);
    }

    private static async Task PlayVisualCueTrigger(Creature creature, string triggerName, float waitTime)
    {
        var creatureNode = creature.GetCreatureNode();
        if (creatureNode == null)
        {
            return;
        }

        creatureNode.SetAnimationTrigger(triggerName);
        await Cmd.CustomScaledWait(Mathf.Min(waitTime * 0.5f, 0.25f), waitTime);
        SoarSpinAnimation.EnsureAirborneSpin(creature);
    }
}
