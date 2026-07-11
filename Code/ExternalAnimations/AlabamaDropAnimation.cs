using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.ExternalAnimations;

public static class AlabamaDropAnimation
{
    private const float ApproachDuration = 0.2f;
    private const float RiseDuration = 0.4f;
    private const float FallDuration = 0.6f;
    private const float StandUpDuration = 0.2f;
    private const float RiseDistance = 900f;
    private const float ApproachGap = 50f;
    private const float ReturnHopHeight = 70f;
    private const float TumbleDegrees = 1800f;
    private const float MinScaleRatio = 0.18f;

    internal const float TumbleDegreesPerSecond = TumbleDegrees / FallDuration;

    public static async Task Play(Creature owner, Creature target, Func<Task> onImpact)
    {
        if (!TryGetRig(owner, out CreatureRig ownerRig) || !TryGetRig(target, out CreatureRig targetRig))
        {
            await CreatureCmd.TriggerAnim(owner, "Cast", owner.Player?.Character.CastAnimDelay ?? 0f);
            await onImpact();
            return;
        }

        var ownerSnapshot = CreatureVisualSnapshot.Capture(ownerRig);
        var targetSnapshot = CreatureVisualSnapshot.Capture(targetRig);
        Vector2 ownerStartPos = ownerRig.CreatureNode.Position;
        Vector2 targetStartPos = targetRig.CreatureNode.Position;
        Vector2 ownerLandingPos = ResolveOwnerLandingPosition(ownerRig, targetRig);
        float ownerInvertedRotation = ownerSnapshot.BodyRotationDegrees + 180f;
        float targetInvertedRotation = targetSnapshot.BodyRotationDegrees + 180f;

        try
        {
            await TweenNodePosition(
                ownerRig.CreatureNode,
                ownerLandingPos,
                ApproachDuration,
                Tween.EaseType.Out,
                Tween.TransitionType.Quad);

            await Task.WhenAll(
                ByrdRiseAnimation.Play(owner, RiseDistance, RiseDuration),
                ByrdRiseAnimation.Play(target, RiseDistance, RiseDuration));

            ownerRig.Body.RotationDegrees = ownerInvertedRotation;
            targetRig.Body.RotationDegrees = targetInvertedRotation;

            NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.NinjaSlayerLongWashoiEvent);
            await Task.WhenAll(
                ByrdFallAnimation.Play(owner, RiseDistance, FallDuration, playImpact: true, onImpact),
                ByrdFallAnimation.Play(target, RiseDistance, FallDuration, playImpact: false),
                PlayEntangledFall(
                    ownerRig,
                    targetRig,
                    ownerSnapshot,
                    targetSnapshot,
                    ownerLandingPos,
                    targetStartPos,
                    ownerInvertedRotation,
                    targetInvertedRotation,
                    FallDuration));

            ownerRig.Body.RotationDegrees = ownerInvertedRotation;
            targetRig.Body.RotationDegrees = targetInvertedRotation;
            ownerRig.Body.Scale = ownerSnapshot.BodyScale;
            targetRig.Body.Scale = targetSnapshot.BodyScale;

            await Task.WhenAll(
                TweenHopNodePosition(ownerRig.CreatureNode, ownerStartPos, StandUpDuration),
                TweenBodyRotation(ownerRig.Body, ownerSnapshot.BodyRotationDegrees + 360f, StandUpDuration),
                TweenBodyRotation(targetRig.Body, targetSnapshot.BodyRotationDegrees + 360f, StandUpDuration));
        }
        finally
        {
            targetSnapshot.Restore(restoreNinjaSlayerAirborneState: false);
            ownerSnapshot.Restore(restoreNinjaSlayerAirborneState: true);
        }
    }

    private static bool TryGetRig(Creature creature, out CreatureRig rig)
    {
        rig = default;

        var creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
        if (creatureNode?.Visuals == null)
        {
            return false;
        }

        Node2D body = creatureNode.Body;
        if (body == null)
        {
            return false;
        }

        rig = new CreatureRig(
            creature,
            creatureNode,
            creatureNode.Visuals,
            body,
            NinjaSlayerVisualRig.GetAirborneAnchor(creatureNode.Visuals));
        return true;
    }

    private static Vector2 ResolveOwnerLandingPosition(CreatureRig ownerRig, CreatureRig targetRig)
    {
        Vector2 ownerPos = ownerRig.CreatureNode.Position;
        Vector2 targetPos = targetRig.CreatureNode.Position;
        float directionToTarget = Mathf.Sign(targetPos.X - ownerPos.X);
        if (Mathf.IsZeroApprox(directionToTarget))
        {
            directionToTarget = 1f;
        }

        float targetHalfWidth = targetRig.Visuals.Bounds.Size.X * Mathf.Abs(targetRig.Visuals.Scale.X) * 0.5f;
        float x = targetPos.X - directionToTarget * (targetHalfWidth + ApproachGap);
        return new Vector2(x, ownerPos.Y);
    }

    private static async Task PlayEntangledFall(
        CreatureRig ownerRig,
        CreatureRig targetRig,
        CreatureVisualSnapshot ownerSnapshot,
        CreatureVisualSnapshot targetSnapshot,
        Vector2 ownerLandingPos,
        Vector2 targetLandingPos,
        float ownerInvertedRotation,
        float targetInvertedRotation,
        float duration)
    {
        float centerX = (ownerLandingPos.X + targetLandingPos.X) * 0.5f;
        float ownerOffsetX = ownerLandingPos.X - centerX;
        float targetOffsetX = targetLandingPos.X - centerX;

        var tween = ownerRig.CreatureNode.CreateTween();
        tween.TweenMethod(
            Callable.From<float>(progress =>
            {
                float radians = Mathf.DegToRad(TumbleDegrees * progress);
                float orbitRatio = Mathf.Cos(radians);
                float scaleRatio = ClampScaleRatio(orbitRatio);

                ownerRig.CreatureNode.Position = new Vector2(centerX + ownerOffsetX * orbitRatio, ownerLandingPos.Y);
                targetRig.CreatureNode.Position = new Vector2(centerX + targetOffsetX * orbitRatio, targetLandingPos.Y);

                ownerRig.Body.RotationDegrees = ownerInvertedRotation;
                targetRig.Body.RotationDegrees = targetInvertedRotation;
                ownerRig.Body.Scale = new Vector2(ownerSnapshot.BodyScale.X * scaleRatio, ownerSnapshot.BodyScale.Y);
                targetRig.Body.Scale = new Vector2(targetSnapshot.BodyScale.X * scaleRatio, targetSnapshot.BodyScale.Y);
            }),
            0f,
            1f,
            duration)
            .SetTrans(Tween.TransitionType.Linear);

        await ownerRig.CreatureNode.ToSignal(tween, Tween.SignalName.Finished);

        ownerRig.CreatureNode.Position = ownerLandingPos;
        targetRig.CreatureNode.Position = targetLandingPos;
        ownerRig.Body.Scale = ownerSnapshot.BodyScale;
        targetRig.Body.Scale = targetSnapshot.BodyScale;
    }

    private static float ClampScaleRatio(float scaleRatio)
    {
        if (Mathf.Abs(scaleRatio) >= MinScaleRatio)
        {
            return scaleRatio;
        }

        return scaleRatio < 0f ? -MinScaleRatio : MinScaleRatio;
    }

    private static async Task TweenNodePosition(
        NCreature creatureNode,
        Vector2 target,
        float duration,
        Tween.EaseType ease,
        Tween.TransitionType transition)
    {
        Vector2 start = creatureNode.Position;
        var tween = creatureNode.CreateTween();
        tween.TweenMethod(
            Callable.From<float>(progress =>
            {
                creatureNode.Position = start.Lerp(target, progress);
            }),
            0f,
            1f,
            duration)
            .SetEase(ease)
            .SetTrans(transition);

        await creatureNode.ToSignal(tween, Tween.SignalName.Finished);
        creatureNode.Position = target;
    }

    private static async Task TweenHopNodePosition(NCreature creatureNode, Vector2 target, float duration)
    {
        Vector2 start = creatureNode.Position;
        var tween = creatureNode.CreateTween();
        tween.TweenMethod(
            Callable.From<float>(progress =>
            {
                float easedProgress = progress * progress * (3f - 2f * progress);
                float yOffset = Mathf.Sin(progress * Mathf.Pi) * ReturnHopHeight;
                Vector2 pos = start.Lerp(target, easedProgress);
                creatureNode.Position = new Vector2(pos.X, pos.Y - yOffset);
            }),
            0f,
            1f,
            duration)
            .SetTrans(Tween.TransitionType.Linear);

        await creatureNode.ToSignal(tween, Tween.SignalName.Finished);
        creatureNode.Position = target;
    }

    private static async Task TweenBodyRotation(Node2D body, float targetDegrees, float duration)
    {
        var tween = body.CreateTween();
        tween.TweenProperty(body, "rotation_degrees", targetDegrees, duration)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Quad);

        await body.ToSignal(tween, Tween.SignalName.Finished);
    }

    private readonly record struct CreatureRig(
        Creature Creature,
        NCreature CreatureNode,
        NCreatureVisuals Visuals,
        Node2D Body,
        Node2D? AirborneAnchor);

    private readonly record struct CreatureVisualSnapshot(
        Creature Creature,
        NCreature CreatureNode,
        NCreatureVisuals Visuals,
        Node2D Body,
        Node2D? AirborneAnchor,
        Vector2 CreaturePosition,
        float CreatureRotationDegrees,
        Vector2 CreatureScale,
        Vector2 VisualsPosition,
        float VisualsRotationDegrees,
        Vector2 VisualsScale,
        Vector2 BodyPosition,
        float BodyRotationDegrees,
        Vector2 BodyScale,
        Vector2? AirborneAnchorPosition,
        bool WasAirborne,
        float AirborneOffset,
        bool WasSpinning)
    {
        public static CreatureVisualSnapshot Capture(CreatureRig rig)
        {
            bool wasAirborne = SoarVisualState.TryGetAirborneOffset(rig.Creature, out float airborneOffset);
            return new CreatureVisualSnapshot(
                rig.Creature,
                rig.CreatureNode,
                rig.Visuals,
                rig.Body,
                rig.AirborneAnchor,
                rig.CreatureNode.Position,
                rig.CreatureNode.RotationDegrees,
                rig.CreatureNode.Scale,
                rig.Visuals.Position,
                rig.Visuals.RotationDegrees,
                rig.Visuals.Scale,
                rig.Body.Position,
                rig.Body.RotationDegrees,
                rig.Body.Scale,
                rig.AirborneAnchor?.Position,
                wasAirborne,
                airborneOffset,
                SoarSpinAnimation.IsSpinning(rig.Creature));
        }

        public void Restore(bool restoreNinjaSlayerAirborneState)
        {
            if (GodotObject.IsInstanceValid(CreatureNode))
            {
                CreatureNode.Position = CreaturePosition;
                CreatureNode.RotationDegrees = CreatureRotationDegrees;
                CreatureNode.Scale = CreatureScale;
            }

            if (GodotObject.IsInstanceValid(Visuals))
            {
                Visuals.Position = VisualsPosition;
                Visuals.RotationDegrees = VisualsRotationDegrees;
                Visuals.Scale = VisualsScale;
            }

            if (AirborneAnchor != null && GodotObject.IsInstanceValid(AirborneAnchor) && AirborneAnchorPosition.HasValue)
            {
                AirborneAnchor.Position = AirborneAnchorPosition.Value;
                HopAnimation.SyncBasePosition(Creature, AirborneAnchorPosition.Value);
            }

            if (GodotObject.IsInstanceValid(Body))
            {
                Body.Position = BodyPosition;
                Body.RotationDegrees = BodyRotationDegrees;
                Body.Scale = BodyScale;
            }

            if (!restoreNinjaSlayerAirborneState)
            {
                return;
            }

            if (WasAirborne)
            {
                SoarVisualState.BeginAirborne(Creature, AirborneOffset);
                if (WasSpinning)
                {
                    SoarSpinAnimation.EnsureAirborneSpin(Creature);
                }
            }
            else
            {
                SoarVisualState.ResetVisualsToGround(Creature);
                SoarSpinAnimation.ResetSpinVisual(Creature);
            }
        }
    }
}
