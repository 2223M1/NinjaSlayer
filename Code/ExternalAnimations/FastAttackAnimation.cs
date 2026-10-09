using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Lifecycle;
using NinjaSlayer.Content;
using NinjaSlayer.Code.Nodes;

namespace NinjaSlayer.Code.ExternalAnimations;

public static class FastAttackAnimation
{
    internal static async Task PlayOutwardLunge(Creature creature, float duration, float direction)
    {
        var creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
        if (creatureNode == null)
        {
            return;
        }

        Vector2 originalPosition = creatureNode.Position;
        NinjaSlayerShadowController.Get(creature)?.BeginAction(ShadowActionKind.Attack, duration, CombatActionTimingRuntime.DamageRecoverySeconds);
        float normalizedDirection = Mathf.Sign(direction);
        if (Mathf.IsZeroApprox(normalizedDirection))
        {
            normalizedDirection = creature.IsPlayer ? 1f : -1f;
        }

        var tween = creatureNode.CreateTween();
        tween.TweenMethod(
                Callable.From<float>(progress =>
                {
                    float xOffset = NinjaSlayerCombatVisuals.AttackLungeDistance
                        * FinisherActionTrajectory.FastProgress(progress)
                        * normalizedDirection;
                    creatureNode.Position = originalPosition + new Vector2(xOffset, 0f);
                }),
                0f,
                1f,
                duration)
            .SetTrans(Tween.TransitionType.Linear);

        if (!await TweenPlayback.AwaitCompletion(tween, creatureNode))
        {
            return;
        }
        creatureNode.Position = originalPosition
            + new Vector2(NinjaSlayerCombatVisuals.AttackLungeDistance * normalizedDirection, 0f);
    }

    public static async Task Play(Creature creature, float waitTime, bool reverseDirection = false)
    {
        float peakSeconds = CombatActionTimingRuntime.TriggerSeconds(
            NinjaSlayerAimPose.IsKick(NinjaSlayerAttackExecution.CurrentPlay?.Card) ? 0.25f : waitTime);
        if (!NinjaSlayerFinisherCinematic.IsMovementOwned(creature)
            && FinisherApproach.TryPlayToPeak(creature, peakSeconds, out Task approach))
        {
            await approach;
            return;
        }
        NinjaSlayerShadowController.Get(creature)?.BeginAction(ShadowActionKind.Attack, peakSeconds, CombatActionTimingRuntime.DamageRecoverySeconds);
        if (NinjaSlayerFinisherCinematic.TryPlayOwnedAction(creature, peakSeconds, out Task action,
                NinjaSlayerCombatVisuals.AttackLungeDistance))
        {
            await action;
            return;
        }

        if (RapidCardPresentationContext.IsActive
            && creature.Player?.Character is INinjaSlayerCharacter)
        {
            await NinjaSlayerRapidAnimationCoordinator.PlayAttackToPeak(
                creature,
                NinjaSlayerCombatVisuals.AttackLungeDistance,
                peakSeconds,
                FinisherActionTrajectory.FastProgress,
                reverseDirection,
                CombatActionTimingRuntime.AttackReturnSeconds(peakSeconds));
            return;
        }

        await SlowAttackAnimation.PlayLunge(creature, NinjaSlayerCombatVisuals.AttackLungeDistance,
            peakSeconds, Math.Min(peakSeconds, 0.075f), CombatActionTimingRuntime.AttackReturnSeconds(peakSeconds),
            FinisherActionTrajectory.FastProgress, reverseDirection);
    }
}
