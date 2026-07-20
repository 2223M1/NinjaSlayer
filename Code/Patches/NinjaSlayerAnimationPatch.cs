using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class NinjaSlayerAnimationPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_animation_trigger";

    public static string Description => "Route NinjaSlayer combat TriggerAnim calls to ExternalAnimations.";

    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(CreatureCmd), nameof(CreatureCmd.TriggerAnim), [typeof(Creature), typeof(string), typeof(float)])];

    public static bool Prefix(Creature creature, string triggerName, float waitTime, ref Task __result)
    {
        NinjaSlayerFinisherCinematic.NotifyPrimaryAttackAnimation(creature, triggerName);

        // ponytail: one rebuild-time switch restores the archived cue animations.
        if (!NinjaSlayerCharacter.OriginalAnimations)
        {
            return true;
        }

        return !NinjaSlayerCombatAnimations.TryPlayTriggerAnim(creature, triggerName, waitTime, ref __result);
    }
}
