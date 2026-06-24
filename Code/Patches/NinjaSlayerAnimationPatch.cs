using ActsFromThePast;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.Patches;

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.TriggerAnim))]
internal static class NinjaSlayerAnimationPatch
{
    private const float DefaultBlockedHitDuration = 0.2f;

    private static bool Prefix(Creature creature, string triggerName, float waitTime, ref Task __result)
    {
        // ponytail: one rebuild-time switch restores the archived cue animations.
        if (!NinjaSlayerCharacter.OriginalAnimations || creature.Player?.Character is not NinjaSlayerCharacter || creature.IsDead)
        {
            return true;
        }

        switch (triggerName)
        {
            case "Attack":
                SfxCmd.Play(creature.Player.Character.AttackSfx);
                __result = FastAttackAnimation.Play(creature);
                return false;
            case "XAttack":
                SfxCmd.Play(creature.Player.Character.AttackSfx);
                _ = FastAttackAnimation.Play(creature);
                return true;
            case "Cast":
                SfxCmd.Play(creature.Player.Character.CastSfx);
                __result = HopAnimation.Play(creature);
                return false;
            case "Hit":
                SfxCmd.Play(NinjaSlayerAudio.CharacterHurtEvent);
                __result = StaggerAnimation.Play(creature);
                return false;
            case "BlockedHit":
                var duration = waitTime > 0f ? waitTime : DefaultBlockedHitDuration;
                __result = ShakeAnimation.Play(creature, duration, duration);
                return false;
            default:
                return true;
        }
    }
}
