using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Content;

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
        float peakSeconds = CombatActionTimingRuntime.AttackSeconds;
        if (NinjaSlayerFinisherCinematic.TryPlayOwnedAction(creature, peakSeconds, out Task action))
        {
            await action;
            return;
        }

        if (NinjaSlayerRapidAnimationCoordinator.IsEnabled
            && creature.Player?.Character is INinjaSlayerCharacter)
        {
            await NinjaSlayerRapidAnimationCoordinator.PlayAttackToPeak(
                creature,
                NinjaSlayerCombatVisuals.AttackLungeDistance,
                peakSeconds,
                FinisherActionTrajectory.FastProgress,
                reverseDirection,
                CombatActionTimingRuntime.DamageRecoverySeconds);
            return;
        }

        var creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
        if (creatureNode == null) return;

        Vector2 originalPos = creatureNode.Position;
        float direction = (creature.Side == CombatSide.Player ? 1f : -1f)
            * (reverseDirection ? -1f : 1f);
        Vector2 peakPosition = originalPos
            + Vector2.Right * NinjaSlayerCombatVisuals.AttackLungeDistance * direction;

        if (!await TweenPosition(
                creatureNode,
                originalPos,
                peakPosition,
                peakSeconds,
                FinisherActionTrajectory.FastProgress))
        {
            return;
        }

        StartReturn(creatureNode, peakPosition, originalPos, CombatActionTimingRuntime.DamageRecoverySeconds);
    }

    private static async Task<bool> TweenPosition(
        Control creatureNode,
        Vector2 start,
        Vector2 end,
        float duration,
        Func<float, float> easing)
    {
        if (Mathf.IsZeroApprox(duration))
        {
            creatureNode.Position = end;
            return true;
        }

        Tween tween = creatureNode.CreateTween();
        tween.TweenMethod(
                Callable.From<float>(progress => creatureNode.Position = start.Lerp(end, easing(progress))),
                0f,
                1f,
                duration)
            .SetTrans(Tween.TransitionType.Linear);
        bool completed = await TweenPlayback.AwaitCompletion(tween, creatureNode);
        if (completed && GodotObject.IsInstanceValid(creatureNode))
        {
            creatureNode.Position = end;
        }

        return completed;
    }

    private static void StartReturn(
        Control creatureNode,
        Vector2 start,
        Vector2 destination,
        float duration)
    {
        if (Mathf.IsZeroApprox(duration))
        {
            Callable.From(() =>
            {
                if (GodotObject.IsInstanceValid(creatureNode))
                {
                    creatureNode.Position = destination;
                }
            }).CallDeferred();
            return;
        }

        Tween tween = creatureNode.CreateTween();
        tween.TweenMethod(
                Callable.From<float>(progress =>
                    creatureNode.Position = start.Lerp(destination, Mathf.SmoothStep(0f, 1f, progress))),
                0f,
                1f,
                duration)
            .SetTrans(Tween.TransitionType.Linear);
        TaskHelper.RunSafely(CompleteReturn(tween, creatureNode, destination));
    }

    private static async Task CompleteReturn(Tween tween, Control creatureNode, Vector2 destination)
    {
        await TweenPlayback.AwaitCompletion(tween, creatureNode);
        if (GodotObject.IsInstanceValid(creatureNode))
        {
            creatureNode.Position = destination;
        }
    }
}
