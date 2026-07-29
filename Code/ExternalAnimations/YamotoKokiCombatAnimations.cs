using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Content;
using NinjaSlayer.Monsters;

namespace NinjaSlayer.Code.ExternalAnimations;

internal enum YamotoKokiIaiApproachMode
{
    StandardLunge,
    FinisherCloseRange
}

internal static class YamotoKokiCombatAnimations
{
    private const float SummonTiltSeconds = 0.1f;
    private const float SummonReturnSeconds = 0.2f;
    private const float TiltDegrees = 15f;
    private const float EntranceOffsetX = -1400f;
    private const float EntranceSeconds = 0.5f;
    private const float FarewellTiltSeconds = 0.5f;
    private const float FarewellHoldSeconds = 0.2f;
    private const float FarewellReturnSeconds = 0.3f;
    private const float FarewellExitSeconds = 0.5f;
    private const float GroundOffsetFromPivot = 8.625f;
    private const float IaiApproachSeconds = 0.25f;
    private const float IaiApproachDistance = 120f;
    private static readonly Vector2 RightFootContactFromPivot = new(65.495f, 3.137f);

    public static bool TryPlayTriggerAnim(
        Creature creature,
        string triggerName,
        float waitTime,
        ref Task result)
    {
        if (creature.Monster is not YamotoKokiMonster || creature.IsDead)
        {
            return false;
        }

        switch (triggerName)
        {
            case "Dodge":
                result = CombatDodgeAnimation.PlayImmediate(creature);
                return true;
            case "Hit":
            case "BlockedHit":
                result = CombatDodgeAnimation.PlayImmediate(creature);
                return true;
            case "SlowAttack":
                result = SlowAttackAnimation.Play(creature);
                return true;
            default:
                return false;
        }
    }

    public static async Task PlaySummon(Creature creature, Func<Task> summonAtPeak)
    {
        NCreature? creatureNode = creature.GetCreatureNode();
        if (creatureNode == null)
        {
            await summonAtPeak();
            return;
        }

        Node2D body = creatureNode.Body;
        Vector2 originalBodyPosition = body.Position;
        float originalRotation = body.RotationDegrees;
        try
        {
            await TweenTilt(
                body,
                originalBodyPosition,
                originalRotation,
                0f,
                TiltDegrees,
                SummonTiltSeconds);
            await summonAtPeak();
            await TweenTilt(
                body,
                originalBodyPosition,
                originalRotation,
                TiltDegrees,
                0f,
                SummonReturnSeconds);
        }
        finally
        {
            if (GodotObject.IsInstanceValid(body))
            {
                body.Position = originalBodyPosition;
                body.RotationDegrees = originalRotation;
            }
        }
    }

    public static async Task PlayIaiSlash(
        Creature creature,
        NCreature targetNode,
        Func<Task> approachStarted,
        Func<Task> impactAtPeak,
        YamotoKokiIaiApproachMode approachMode)
    {
        NCreature? creatureNode = creature.GetCreatureNode();
        if (creatureNode == null)
        {
            await impactAtPeak();
            return;
        }

        Vector2 originalPosition = creatureNode.Position;
        bool isFinisherApproach = approachMode == YamotoKokiIaiApproachMode.FinisherCloseRange;
        if (isFinisherApproach)
        {
            await approachStarted();
            if (NinjaSlayerFinisherCinematic.TryPlayOwnedAction(
                    creature,
                    IaiApproachSeconds,
                    out Task action))
            {
                await action;
            }
            else
            {
                await Cmd.Wait(IaiApproachSeconds);
            }

            await impactAtPeak();
            return;
        }

        float direction = creature.Side == CombatSide.Player ? 1f : -1f;
        if (Mathf.IsZeroApprox(direction))
        {
            direction = 1f;
        }

        Vector2 approachStart = originalPosition;
        Vector2 impactPosition = originalPosition + Vector2.Right * direction * IaiApproachDistance;

        try
        {
            await approachStarted();
            await TweenIaiApproach(creatureNode, approachStart, impactPosition);
            await impactAtPeak();
            await TweenIaiReturn(creatureNode, impactPosition, originalPosition);
        }
        finally
        {
            if (GodotObject.IsInstanceValid(creatureNode))
            {
                creatureNode.Position = originalPosition;
            }
        }
    }

    public static async Task PlayEntrance(Creature creature)
    {
        NCreature? creatureNode = creature.GetCreatureNode();
        if (creatureNode == null)
        {
            return;
        }

        Vector2 destination = creatureNode.Position;
        creatureNode.Hide();
        creatureNode.Position = destination + new Vector2(EntranceOffsetX, 0f);
        creatureNode.Show();
        NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.YamotoKokiGoEvent);

        try
        {
            await TweenPosition(
                creatureNode,
                destination,
                EntranceSeconds,
                Tween.EaseType.Out,
                Tween.TransitionType.Quad);
        }
        finally
        {
            if (GodotObject.IsInstanceValid(creatureNode))
            {
                creatureNode.Position = destination;
                creatureNode.Show();
            }
        }
    }

    public static async Task PlayFarewell(Creature creature)
    {
        NCreature? creatureNode = creature.GetCreatureNode();
        if (creatureNode == null)
        {
            return;
        }

        Node2D body = creatureNode.Body;
        Vector2 originalPosition = creatureNode.Position;
        Vector2 originalBodyPosition = body.Position;
        float originalRotation = body.RotationDegrees;

        try
        {
            await TweenTilt(
                body,
                originalBodyPosition,
                originalRotation,
                0f,
                TiltDegrees,
                FarewellTiltSeconds);
            await Cmd.Wait(FarewellHoldSeconds, ignoreCombatEnd: true);
            await TweenTilt(
                body,
                originalBodyPosition,
                originalRotation,
                TiltDegrees,
                0f,
                FarewellReturnSeconds);
            await TweenPosition(
                creatureNode,
                originalPosition + new Vector2(EntranceOffsetX, 0f),
                FarewellExitSeconds,
                Tween.EaseType.In,
                Tween.TransitionType.Quad);
            creatureNode.Hide();
        }
        finally
        {
            if (GodotObject.IsInstanceValid(body))
            {
                body.Position = originalBodyPosition;
                body.RotationDegrees = originalRotation;
            }

            if (GodotObject.IsInstanceValid(creatureNode) && creatureNode.Visible)
            {
                creatureNode.Position = originalPosition;
            }
        }
    }

    private static async Task TweenTilt(
        Node2D node,
        Vector2 basePosition,
        float baseRotation,
        float fromDegrees,
        float toDegrees,
        float duration)
    {
        if (!GodotObject.IsInstanceValid(node))
        {
            return;
        }

        Tween tween = node.CreateTween();
        tween.TweenMethod(
                Callable.From<float>(progress =>
                {
                    float tiltDegrees = Mathf.Lerp(fromDegrees, toDegrees, progress);
                    node.RotationDegrees = baseRotation + tiltDegrees;
                    float facingSign = FacingScaleMath.IsFacingLeft(node.Scale.X) ? -1f : 1f;
                    Vector2 footContact = new(
                        RightFootContactFromPivot.X * facingSign,
                        RightFootContactFromPivot.Y);
                    Vector2 rotatedFoot = footContact.Rotated(
                        Mathf.DegToRad(tiltDegrees));
                    float groundY = basePosition.Y + GroundOffsetFromPivot;
                    float footY = basePosition.Y + rotatedFoot.Y;
                    float lift = Mathf.Max(0f, footY - groundY);
                    node.Position = basePosition + Vector2.Up * lift;
                }),
                0f,
                1f,
                duration)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Quad);
        await node.ToSignal(tween, Tween.SignalName.Finished);
    }

    private static async Task TweenIaiApproach(
        NCreature creatureNode,
        Vector2 start,
        Vector2 destination)
    {
        Tween tween = creatureNode.CreateTween();
        tween.TweenMethod(
                Callable.From<float>(progress =>
                {
                    if (GodotObject.IsInstanceValid(creatureNode))
                    {
                        creatureNode.Position = start.Lerp(
                            destination,
                            FinisherActionTrajectory.SlowProgress(progress));
                    }
                }),
                0f,
                1f,
                IaiApproachSeconds)
            .SetTrans(Tween.TransitionType.Linear);
        await creatureNode.ToSignal(tween, Tween.SignalName.Finished);
    }

    private static async Task TweenIaiReturn(
        NCreature creatureNode,
        Vector2 start,
        Vector2 destination)
    {
        Tween tween = creatureNode.CreateTween();
        tween.TweenMethod(
                Callable.From<float>(progress =>
                {
                    if (GodotObject.IsInstanceValid(creatureNode))
                    {
                        creatureNode.Position = start.Lerp(
                            destination,
                            Mathf.SmoothStep(0f, 1f, progress));
                    }
                }),
                0f,
                1f,
                IaiApproachSeconds)
            .SetTrans(Tween.TransitionType.Linear);
        await creatureNode.ToSignal(tween, Tween.SignalName.Finished);
    }

    private static async Task TweenPosition(
        Control node,
        Vector2 target,
        float duration,
        Tween.EaseType ease,
        Tween.TransitionType transition)
    {
        if (!GodotObject.IsInstanceValid(node))
        {
            return;
        }

        Tween tween = node.CreateTween();
        tween.TweenProperty(node, new NodePath("position"), target, duration)
            .SetEase(ease)
            .SetTrans(transition);
        await node.ToSignal(tween, Tween.SignalName.Finished);
    }
}
