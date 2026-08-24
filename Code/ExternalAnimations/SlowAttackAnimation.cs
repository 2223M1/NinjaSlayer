using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Lifecycle;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.ExternalAnimations;

public static class SlowAttackAnimation
{
    private const float LungeDistance = NinjaSlayerCombatVisuals.SlowAttackLungeDistance;

    internal static float PeakSeconds => CombatActionTimingRuntime.SlowAttackSeconds;
    internal static float CompanionPeakSeconds => CombatActionTimingRuntime.CompanionSlowAttackSeconds;
    public static Task Play(Creature creature) => Play(
        creature,
        PeakSeconds,
        CombatActionTimingRuntime.DamageRecoverySeconds,
        null);

    internal static Task PlayRoundTrip(Creature creature, Func<Task> impactAtPeak) =>
        Play(
            creature,
            CompanionPeakSeconds,
            CombatActionTimingRuntime.CompanionDamageRecoverySeconds,
            impactAtPeak);

    private static async Task Play(
        Creature creature,
        float peakSeconds,
        float returnSeconds,
        Func<Task>? impactAtPeak)
    {
        if (NinjaSlayerFinisherCinematic.TryPlayOwnedAction(creature, peakSeconds, out Task action))
        {
            await action;
            if (impactAtPeak != null)
            {
                await impactAtPeak();
            }
            return;
        }

        if (impactAtPeak == null
            && RapidCardPresentationContext.IsActive
            && creature.Player?.Character is INinjaSlayerCharacter)
        {
            await NinjaSlayerRapidAnimationCoordinator.PlayAttackToPeak(
                creature,
                LungeDistance,
                peakSeconds,
                FinisherActionTrajectory.SlowProgress,
                returnSeconds: returnSeconds);
            return;
        }

        var creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
        if (creatureNode == null)
        {
            if (impactAtPeak != null)
            {
                await impactAtPeak();
            }
            return;
        }

        _ = StaggerAnimation.TryTakeover(creature, out StaggerAnimation.HandoffLease? handoff);
        Vector2 baseline = handoff?.BaselinePosition ?? creatureNode.Position;
        Vector2 start = handoff?.CurrentPosition ?? creatureNode.Position;
        float direction = creature.Side == CombatSide.Player ? 1f : -1f;
        Vector2 peak = baseline + Vector2.Right * LungeDistance * direction;
        bool reachedPeak = await TweenAttackToPeak(
            creatureNode,
            start,
            peak,
            peakSeconds,
            handoff);

        if (impactAtPeak == null)
        {
            if (!reachedPeak)
            {
                handoff?.Restore();
                return;
            }

            _ = TaskHelper.RunSafely(ReturnToBaseline(
                creatureNode,
                peak,
                baseline,
                returnSeconds,
                handoff));
            return;
        }

        try
        {
            await impactAtPeak();
        }
        finally
        {
            if (reachedPeak)
            {
                await ReturnToBaseline(
                    creatureNode,
                    peak,
                    baseline,
                    returnSeconds,
                    handoff);
            }
            else
            {
                handoff?.Restore();
            }
        }
    }

    internal static async Task PlayCombo(
        Creature creature,
        int hitCount,
        float firstPeakDuration,
        float hitSpacing,
        float returnDuration,
        Func<Task> impactAtPeak)
    {
        if (hitCount <= 0)
        {
            return;
        }

        var creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
        if (creatureNode == null)
        {
            for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
            {
                await impactAtPeak();
                if (hitIndex + 1 < hitCount)
                {
                    await Cmd.Wait(hitSpacing);
                }
            }
            return;
        }

        Vector2 originalPos = creatureNode.Position;
        float direction = creature.Side == CombatSide.Player ? 1f : -1f;
        float peakOffset = LungeDistance * direction;
        float retreatOffset = peakOffset * 0.5f;
        try
        {
            if (!await TweenOffset(
                    creatureNode,
                    originalPos,
                    0f,
                    peakOffset,
                    firstPeakDuration,
                    outbound: true))
            {
                return;
            }

            await impactAtPeak();
            for (int hitIndex = 1; hitIndex < hitCount; hitIndex++)
            {
                float retreatDuration = hitSpacing * 0.5f;
                if (!await TweenOffset(
                        creatureNode,
                        originalPos,
                        peakOffset,
                        retreatOffset,
                        retreatDuration,
                        outbound: false)
                    || !await TweenOffset(
                        creatureNode,
                        originalPos,
                        retreatOffset,
                        peakOffset,
                        hitSpacing - retreatDuration,
                        outbound: true))
                {
                    return;
                }

                await impactAtPeak();
            }

            await TweenOffset(
                creatureNode,
                originalPos,
                peakOffset,
                0f,
                returnDuration,
                outbound: false);
        }
        finally
        {
            if (GodotObject.IsInstanceValid(creatureNode))
            {
                creatureNode.Position = originalPos;
            }
        }
    }

    private static async Task<bool> TweenAttackToPeak(
        Control creatureNode,
        Vector2 start,
        Vector2 peak,
        float duration,
        StaggerAnimation.HandoffLease? handoff)
    {
        if (Mathf.IsZeroApprox(duration))
        {
            creatureNode.Position = peak;
            ApplyBodyRecovery(handoff, 1f);
            return true;
        }

        Tween tween = creatureNode.CreateTween();
        tween.TweenMethod(
                Callable.From<float>(progress =>
                {
                    float eased = FinisherActionTrajectory.SlowProgress(progress);
                    creatureNode.Position = start.Lerp(peak, eased);
                    ApplyBodyRecovery(handoff, eased);
                }),
                0f,
                1f,
                duration)
            .SetTrans(Tween.TransitionType.Linear);
        bool completed = await TweenPlayback.AwaitCompletion(tween, creatureNode);
        if (completed && GodotObject.IsInstanceValid(creatureNode))
        {
            creatureNode.Position = peak;
            ApplyBodyRecovery(handoff, 1f);
        }

        return completed;
    }

    private static Task ReturnToBaseline(
        Control creatureNode,
        Vector2 start,
        Vector2 destination,
        float duration,
        StaggerAnimation.HandoffLease? handoff)
    {
        if (!GodotObject.IsInstanceValid(creatureNode) || !creatureNode.IsInsideTree())
        {
            handoff?.Restore();
            return Task.CompletedTask;
        }

        if (Mathf.IsZeroApprox(duration))
        {
            creatureNode.Position = destination;
            handoff?.Restore();
            return Task.CompletedTask;
        }

        Tween tween = creatureNode.CreateTween();
        tween.TweenMethod(
                Callable.From<float>(progress =>
                    creatureNode.Position = start.Lerp(destination, Mathf.SmoothStep(0f, 1f, progress))),
                0f,
                1f,
                duration)
            .SetTrans(Tween.TransitionType.Linear);
        return CompleteReturn(tween, creatureNode, destination, handoff);
    }

    private static async Task CompleteReturn(
        Tween tween,
        Control creatureNode,
        Vector2 destination,
        StaggerAnimation.HandoffLease? handoff)
    {
        try
        {
            await TweenPlayback.AwaitCompletion(tween, creatureNode);
        }
        finally
        {
            if (GodotObject.IsInstanceValid(creatureNode))
            {
                creatureNode.Position = destination;
            }
            handoff?.Restore();
        }
    }

    private static void ApplyBodyRecovery(StaggerAnimation.HandoffLease? handoff, float progress)
    {
        if (handoff?.BodyAnchor is not { } body || !GodotObject.IsInstanceValid(body))
        {
            return;
        }

        body.RotationDegrees = Mathf.Lerp(
            handoff.CurrentBodyRotation,
            handoff.BaselineBodyRotation,
            progress);
    }

    private static async Task<bool> TweenOffset(
        Control creatureNode,
        Vector2 originalPos,
        float fromOffset,
        float toOffset,
        float duration,
        bool outbound)
    {
        if (!GodotObject.IsInstanceValid(creatureNode))
        {
            return false;
        }

        if (Mathf.IsZeroApprox(duration))
        {
            creatureNode.Position = originalPos + new Vector2(toOffset, 0f);
            return true;
        }

        Tween tween = creatureNode.CreateTween();
        tween.TweenMethod(
                Callable.From<float>(progress =>
                {
                    float easedProgress = outbound
                        ? FinisherActionTrajectory.SlowProgress(progress)
                        : Mathf.SmoothStep(0f, 1f, progress);
                    creatureNode.Position = originalPos
                        + new Vector2(Mathf.Lerp(fromOffset, toOffset, easedProgress), 0f);
                }),
                0f,
                1f,
                duration)
            .SetTrans(Tween.TransitionType.Linear);
        return await TweenPlayback.AwaitCompletion(tween, creatureNode);
    }
}
