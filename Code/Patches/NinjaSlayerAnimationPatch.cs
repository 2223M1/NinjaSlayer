using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using NinjaSlayer.Monsters;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class NinjaSlayerAnimationPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_animation_trigger";

    public static string Description => "Route NinjaSlayer combat TriggerAnim calls to ExternalAnimations.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(CreatureCmd), nameof(CreatureCmd.TriggerAnim), [typeof(Creature), typeof(string), typeof(float)])];

    public static bool Prefix(Creature creature, string triggerName, float waitTime, ref Task __result, out bool __state)
    {
        __state = false;
        GrappledTargetPose.Release(creature);
        if (triggerName == "Hit" && DarkStrikeHurtPoseFreezeContext.TryDeferHit(creature))
        {
            __state = true;
            __result = Task.CompletedTask;
            return false;
        }
        DarkNinjaSpecialAttackPresentation.CancelDeferredHurt(creature);
        FinisherAttackVfxBaselineContext.BeginApproach(creature, triggerName, waitTime);
        Nodes.TornadoHurtPause.Cancel(creature);
        if (creature.Monster is SawatariMonster && creature.Side == CombatSide.Enemy && creature.IsAlive)
        {
            if (triggerName == "Hit")
            {
                _ = StaggerAnimation.Play(creature, StaggerAnimation.MirroredRotationDegrees);
                __result = Task.CompletedTask;
                return false;
            }
            if (triggerName == "BlockedHit")
            {
                ShakeAnimation.PlayNonBlocking(creature, waitTime > 0f ? waitTime : 0.2f);
                __result = Task.CompletedTask;
                return false;
            }
        }
        if (YamotoKokiCombatAnimations.TryPlayTriggerAnim(creature, triggerName, waitTime, ref __result))
        {
            return false;
        }

        if (DarkNinjaCombatAnimations.TryPlayTriggerAnim(creature, triggerName, waitTime, ref __result))
        {
            return false;
        }

        FinisherSessionRegistry.GetActiveSession()?.NotifyPrimaryAttackAnimation(creature, triggerName);

        return !NinjaSlayerCombatAnimations.TryPlayTriggerAnim(creature, triggerName, waitTime, ref __result);
    }

    public static void Postfix(Creature creature, string triggerName, bool __state)
    {
        if (!__state) TornadoFistSpinAnimation.NotifyHitTriggered(creature, triggerName);
    }
}
