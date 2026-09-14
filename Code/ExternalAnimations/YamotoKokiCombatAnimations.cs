using Godot;
using NinjaSlayer.Code.Nodes;
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

    public static bool TryPlayTriggerAnim(
        Creature creature,
        string triggerName,
        float waitTime,
        ref Task result)
    {
        bool isYamotoKoki = creature.Monster is YamotoKokiMonster;
        bool isSawatariCompanion = creature.Monster is SawatariMonster
            && creature.Side == CombatSide.Player
            && creature.PetOwner != null;
        bool isYukanoCompanion = creature.Monster is YukanoMonster
            && creature.Side == CombatSide.Player
            && creature.PetOwner != null;
        if ((!isYamotoKoki && !isSawatariCompanion && !isYukanoCompanion)
            || creature.IsDead)
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
            case "SlowAttack" when isYamotoKoki:
                result = SlowAttackAnimation.PlayIai(creature);
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
        Marker2D center = creatureNode.Visuals.VfxSpawnPosition;
        Vector2 originalCenter = center.Position;
        Transform2D originalBody = body.Transform;
        try
        {
            await TweenTilt(
                body,
                center, originalCenter,
                originalBody,
                0f,
                TiltDegrees,
                SummonTiltSeconds);
            await summonAtPeak();
            await TweenTilt(
                body,
                center, originalCenter,
                originalBody,
                TiltDegrees,
                0f,
                SummonReturnSeconds);
        }
        finally
        {
            if (GodotObject.IsInstanceValid(center)) center.Position = originalCenter;
            if (GodotObject.IsInstanceValid(body))
            {
                body.Transform = YamotoKokiAllyFacingController.WithFacing(
                    originalBody, body.Transform.Determinant() < 0f);
            }
        }
    }

    public static async Task PlayIaiSlash(
        Creature creature,
        Func<Task> approachStarted,
        Func<Task> impactAtPeak,
        YamotoKokiIaiApproachMode approachMode)
    {
        bool isFinisherApproach = approachMode == YamotoKokiIaiApproachMode.FinisherCloseRange;
        if (isFinisherApproach)
        {
            NinjaSlayerShadowController.Get(creature)?.BeginAction(
                ShadowActionKind.SlowAttack, SlowAttackAnimation.IaiPeakSeconds,
                SlowAttackAnimation.IaiReturnSeconds);
            await approachStarted();
            if (NinjaSlayerFinisherCinematic.TryPlayOwnedAction(
                    creature,
                    SlowAttackAnimation.IaiPeakSeconds,
                    out Task action))
            {
                await action;
            }
            else
            {
                await Cmd.Wait(SlowAttackAnimation.IaiPeakSeconds);
            }

            await impactAtPeak();
            return;
        }

        await approachStarted();
        await SlowAttackAnimation.PlayIai(creature);
        await impactAtPeak();
    }

    public static async Task PlayEntrance(Creature creature, bool playVoice = true)
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
        if (playVoice)
        {
            NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.YamotoKokiGoEvent);
        }

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
        Marker2D center = creatureNode.Visuals.VfxSpawnPosition;
        Vector2 originalCenter = center.Position;
        Vector2 originalPosition = creatureNode.Position;
        Transform2D originalBody = body.Transform;

        try
        {
            await TweenTilt(
                body,
                center, originalCenter,
                originalBody,
                0f,
                TiltDegrees,
                FarewellTiltSeconds);
            await Cmd.Wait(FarewellHoldSeconds, ignoreCombatEnd: true);
            await TweenTilt(
                body,
                center, originalCenter,
                originalBody,
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
            if (GodotObject.IsInstanceValid(center)) center.Position = originalCenter;
            if (GodotObject.IsInstanceValid(body))
            {
                body.Transform = YamotoKokiAllyFacingController.WithFacing(
                    originalBody, body.Transform.Determinant() < 0f);
            }

            if (GodotObject.IsInstanceValid(creatureNode) && creatureNode.Visible)
            {
                creatureNode.Position = originalPosition;
            }
        }
    }

    private static async Task TweenTilt(
        Node2D node,
        Marker2D center,
        Vector2 centerBaseline,
        Transform2D authoredBody,
        float fromDegrees,
        float toDegrees,
        float duration)
    {
        if (!GodotObject.IsInstanceValid(node))
        {
            return;
        }

        Sprite2D sprite = node.GetChildren().OfType<Sprite2D>().First();
        Transform2D parentCanvas = node.GetParent<CanvasItem>().GetGlobalTransformWithCanvas();
        Vector2 pivot = parentCanvas.AffineInverse()
            * (center.GetParent<CanvasItem>().GetGlobalTransformWithCanvas() * centerBaseline);
        Vector2[] bodyPoints = CombatBodyContours.YamotoKoki.Select(point =>
        {
            Vector2 pixel = new(point.X * (sprite.FlipH ? -1f : 1f), point.Y);
            return sprite.Transform * (pixel + sprite.Offset);
        }).ToArray();
        var offsets = new System.Numerics.Vector2[bodyPoints.Length];
        Tween tween = node.CreateTween();
        tween.TweenMethod(
                Callable.From<float>(progress =>
                {
                    Transform2D baseline = YamotoKokiAllyFacingController.WithFacing(
                        authoredBody, node.Transform.Determinant() < 0f);
                    for (int index = 0; index < bodyPoints.Length; index++)
                    {
                        Vector2 offset = baseline * bodyPoints[index] - pivot;
                        offsets[index] = new(offset.X, offset.Y);
                    }
                    float ground = pivot.Y + GroundedPoseMath.SupportY(offsets, 0f);
                    float tiltDegrees = Mathf.Lerp(fromDegrees, toDegrees, progress);
                    float angle = Mathf.DegToRad(tiltDegrees);
                    Vector2 posedCore = new(pivot.X, ground - GroundedPoseMath.SupportY(offsets, angle));
                    Transform2D rotation = new(angle, Vector2.Zero);
                    rotation.Origin = posedCore - rotation.BasisXform(pivot);
                    node.Transform = rotation * baseline;
                    center.Position = center.GetParent<CanvasItem>().GetGlobalTransformWithCanvas()
                        .AffineInverse() * (parentCanvas * posedCore);
                }),
                0f,
                1f,
                duration)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Quad);
        await TweenPlayback.AwaitCompletion(tween, node);
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
        await TweenPlayback.AwaitCompletion(tween, node);
    }
}
