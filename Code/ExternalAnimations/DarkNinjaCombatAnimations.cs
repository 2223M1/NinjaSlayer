using MegaCrit.Sts2.Core.Entities.Creatures;
using NinjaSlayer.Content;
using NinjaSlayer.Monsters;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class DarkNinjaCombatAnimations
{
    private const float DefaultBlockedHitDuration = 0.2f;

    internal static bool TryPlayTriggerAnim(
        Creature creature,
        string triggerName,
        float waitTime,
        ref Task result)
    {
        if (creature.Monster is not DarkNinjaMonster || creature.IsDead)
        {
            return false;
        }

        switch (triggerName)
        {
            case "Attack":
                NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.DarkNinjaFastAttackEvent);
                result = FastAttackAnimation.Play(creature, waitTime);
                return true;
            case "SlowAttack":
                result = SlowAttackAnimation.Play(creature);
                return true;
            case "Hit":
                NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.DarkNinjaHurtEvent);
                _ = StaggerAnimation.Play(creature, StaggerAnimation.MirroredRotationDegrees);
                result = Task.CompletedTask;
                return true;
            case "BlockedHit":
                float duration = waitTime > 0f ? waitTime : DefaultBlockedHitDuration;
                _ = ShakeAnimation.Play(creature, duration, duration);
                result = Task.CompletedTask;
                return true;
            default:
                return false;
        }
    }
}
