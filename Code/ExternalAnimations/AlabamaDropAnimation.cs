using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.ExternalAnimations;

public static class AlabamaDropAnimation
{
    internal const float LungeDuration = 0.05f;
    internal const float GrabHoldDuration = 0.1f;
    internal const float CompressionDuration = 0.1f;
    internal const float RiseDuration = 0.25f;
    private const float FallDuration = 0.6f;
    private const float StandUpDuration = 0.2f;
    private const float RiseDistance = 900f;
    private const float ReturnHopHeight = 70f;
    private const float TumbleAngleCoefficient = 1800f;
    private const float LandingSquashScaleX = 1.2f;
    private const float LandingSquashScaleY = 0.55f;
    private const float LandingSquashHoldDuration = 0.1f;
    private const float GrabHitSparkScale = 0.5f;
    private const float ImpactFireBurstReferenceSize = 450f;
    private const float MinImpactFireBurstScale = 0.4f;
    private const float MaxImpactFireBurstScale = 1f;

    internal const float InitialTumbleDegreesPerSecond = 3000f;
    internal const float FinalTumbleDegreesPerSecond = 12000f;
    internal const float TotalTumbleDegrees = 4320f;

    public static async Task Play(Creature owner, Creature target, Func<Task> onImpact)
    {
        bool doomPoseFrozen = false;
        bool impactPlayed = false;
        bool impactResolutionStarted = false;
        bool impactResolutionJoined = false;
        bool targetBodyDisabled = false;
        Task impactResolutionTask = Task.CompletedTask;
        Node2D? targetBody = null;
        Node.ProcessModeEnum targetBodyProcessMode = Node.ProcessModeEnum.Inherit;

        void RestoreTargetBodyProcessMode()
        {
            if (!targetBodyDisabled)
            {
                return;
            }

            if (targetBody != null && GodotObject.IsInstanceValid(targetBody))
            {
                targetBody.ProcessMode = targetBodyProcessMode;
            }

            targetBodyDisabled = false;
        }

        async Task PlayImpact()
        {
            if (impactPlayed)
            {
                return;
            }

            impactPlayed = true;
            SfxCmd.PlayDamage(target.Monster, 0);
            NCreature? targetNode = NCombatRoom.Instance?.GetCreatureNode(target);
            if (targetNode != null)
            {
                DoomHurtPoseController.Resume(targetNode);
            }
            doomPoseFrozen = false;
            await CreatureCmd.TriggerAnim(target, "Hit", 0f);
            if (targetBody != null && GodotObject.IsInstanceValid(targetBody))
            {
                targetBody.ProcessMode = Node.ProcessModeEnum.Disabled;
                targetBodyDisabled = true;
            }
            PlayImpactFireBurst(target);
            impactResolutionTask = onImpact();
            impactResolutionStarted = true;
        }

        if (!TryGetRig(owner, out CreatureRig ownerRig) || !TryGetRig(target, out CreatureRig targetRig))
        {
            await CreatureCmd.TriggerAnim(owner, "Cast", owner.Player?.Character.CastAnimDelay ?? 0f);
            await PlayImpact();
            if (impactResolutionStarted)
            {
                await impactResolutionTask;
            }
            return;
        }

        targetBody = targetRig.Body;
        targetBodyProcessMode = targetRig.Body.ProcessMode;
        var ownerRestoreSnapshot = CreatureVisualSnapshot.Capture(ownerRig);
        var targetSnapshot = CreatureVisualSnapshot.Capture(targetRig);
        SoarSpinAnimation.SuspendForCinematic(owner);
        var ownerSnapshot = CreatureVisualSnapshot.Capture(ownerRig);
        BodyPivotCompensation ownerPivot = BodyPivotCompensation.Capture(ownerRig);
        BodyPivotCompensation targetPivot = BodyPivotCompensation.Capture(targetRig);
        Vector2 ownerStartPos = ownerRig.CreatureNode.Position;
        Vector2 targetStartPos = targetRig.CreatureNode.Position;
        Vector2 ownerLandingPos = ResolveOwnerLandingPosition(ownerRig, targetRig);
        Vector2 ownerChargeScale = new(
            ownerSnapshot.BodyScale.X * LandingSquashScaleX,
            ownerSnapshot.BodyScale.Y * LandingSquashScaleY);
        Vector2 targetChargeScale = new(
            targetSnapshot.BodyScale.X * LandingSquashScaleX,
            targetSnapshot.BodyScale.Y * LandingSquashScaleY);
        Vector2 targetLandingScale = new(
            targetSnapshot.BodyScale.X * LandingSquashScaleX,
            targetSnapshot.BodyScale.Y * LandingSquashScaleY);
        float ownerInvertedRotation = ownerSnapshot.BodyRotationDegrees + 180f;
        float targetInvertedRotation = targetSnapshot.BodyRotationDegrees + 180f;

        try
        {
            float directionToTarget = Mathf.Sign(targetStartPos.X - ownerStartPos.X);
            await FastAttackAnimation.PlayOutwardLunge(owner, LungeDuration, directionToTarget);
            ownerRig.CreatureNode.Position = ownerLandingPos;

            PlayGrabFeedback(target);
            NGame.Instance?.ScreenShake(ShakeStrength.Weak, ShakeDuration.Short);
            doomPoseFrozen = DoomHurtPoseController.TryFreeze(targetRig.CreatureNode);
            await WaitTweenInterval(ownerRig.CreatureNode, GrabHoldDuration);

            await TweenBodyScales(
                ownerRig.CreatureNode,
                ownerPivot,
                ownerSnapshot.BodyRotationDegrees,
                ownerSnapshot.BodyScale,
                ownerChargeScale,
                targetPivot,
                targetSnapshot.BodyRotationDegrees,
                targetSnapshot.BodyScale,
                targetChargeScale,
                CompressionDuration);

            RestoreBodyTransform(ownerRig.Body, ownerSnapshot);
            RestoreBodyTransform(targetRig.Body, targetSnapshot);

            await Task.WhenAll(
                ByrdRiseAnimation.Play(
                    owner,
                    RiseDistance,
                    RiseDuration,
                    Tween.EaseType.Out,
                    Tween.TransitionType.Expo),
                ByrdRiseAnimation.Play(
                    target,
                    RiseDistance,
                    RiseDuration,
                    Tween.EaseType.Out,
                    Tween.TransitionType.Expo));

            ownerRig.Body.RotationDegrees = ownerInvertedRotation;
            targetPivot.Apply(targetInvertedRotation, targetSnapshot.BodyScale);

            NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.NinjaSlayerLongWashoiEvent);
            await Task.WhenAll(
                ByrdFallAnimation.Play(owner, RiseDistance, FallDuration, playImpact: true, PlayImpact),
                ByrdFallAnimation.Play(target, RiseDistance, FallDuration, playImpact: false),
                PlayEntangledFall(
                    ownerRig,
                    targetRig,
                    FallDuration));

            ownerRig.Body.RotationDegrees = ownerInvertedRotation;
            ownerRig.Body.Scale = ownerSnapshot.BodyScale;
            targetPivot.Apply(targetInvertedRotation, targetLandingScale);
            await WaitTweenInterval(ownerRig.CreatureNode, LandingSquashHoldDuration);
            RestoreTargetBodyProcessMode();

            Task standUpTask = Task.WhenAll(
                TweenHopNodePosition(ownerRig.CreatureNode, ownerStartPos, StandUpDuration),
                TweenBodyRotation(ownerRig.Body, ownerSnapshot.BodyRotationDegrees + 360f, StandUpDuration),
                TweenBodyRotation(
                    targetRig.Body,
                    targetSnapshot.BodyRotationDegrees + 360f,
                    StandUpDuration,
                    targetPivot,
                    targetSnapshot.BodyScale));
            impactResolutionJoined = true;
            await Task.WhenAll(standUpTask, impactResolutionTask);
        }
        finally
        {
            RestoreTargetBodyProcessMode();
            if (impactResolutionStarted && !impactResolutionJoined)
            {
                _ = TaskHelper.RunSafely(impactResolutionTask);
            }

            if (doomPoseFrozen)
            {
                DoomHurtPoseController.Resume(targetRig.CreatureNode);
            }

            targetSnapshot.Restore(restoreNinjaSlayerAirborneState: false);
            ownerRestoreSnapshot.Restore(restoreNinjaSlayerAirborneState: true);
        }
    }

    private static void PlayImpactFireBurst(Creature target)
    {
        try
        {
            NCreature? targetNode = NCombatRoom.Instance?.GetCreatureNode(target);
            float scale = targetNode?.Visuals is { } visuals
                ? CalculateImpactFireBurstScale(visuals.Bounds.Size, visuals.Scale)
                : MaxImpactFireBurstScale;
            NFireBurstVfx? vfx = NFireBurstVfx.Create(target, scale);
            if (vfx is not null && NCombatRoom.Instance is { } room)
            {
                room.CombatVfxContainer.AddChildSafely(vfx);
            }
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"Failed to play Alabama Drop impact fire burst: {ex}");
        }
    }

    private static void PlayGrabFeedback(Creature target)
    {
        try
        {
            if (NCombatRoom.Instance is { } room
                && NHitSparkVfx.Create(target, requireInteractable: false) is { } hitSpark)
            {
                hitSpark.Scale = Vector2.One * GrabHitSparkScale;
                room.CombatVfxContainer.AddChildSafely(hitSpark);
            }
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"Failed to play Alabama Drop grab VFX: {ex}");
        }

        SfxCmd.PlayDamage(target.Monster, 0);
    }

    internal static float CalculateImpactFireBurstScale(Vector2 boundsSize, Vector2 visualsScale)
    {
        float width = boundsSize.X * Mathf.Abs(visualsScale.X);
        float height = boundsSize.Y * Mathf.Abs(visualsScale.Y);
        if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0f || height <= 0f)
        {
            return MaxImpactFireBurstScale;
        }

        float sizeMetric = Mathf.Sqrt(width * height);
        if (!float.IsFinite(sizeMetric))
        {
            return MaxImpactFireBurstScale;
        }

        return Mathf.Clamp(
            sizeMetric / ImpactFireBurstReferenceSize,
            MinImpactFireBurstScale,
            MaxImpactFireBurstScale);
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
        float x = targetPos.X - directionToTarget
            * (targetHalfWidth + NinjaSlayerCombatVisuals.CloseRangeApproachGap);
        return new Vector2(x, ownerPos.Y);
    }

    private static async Task PlayEntangledFall(
        CreatureRig ownerRig,
        CreatureRig targetRig,
        float duration)
    {
        Vector2 ownerCenter = ResolveOwnerSpinCenter(ownerRig);
        Vector2 targetCenter = targetRig.Visuals.Bounds.GetGlobalRect().GetCenter();
        float sharedAxisX = (ownerCenter.X + targetCenter.X) * 0.5f;
        var ownerProjection = VerticalAxisSpinProjection.CaptureCurrent(
            ownerRig.Body,
            sharedAxisX,
            ownerCenter);
        var targetProjection = VerticalAxisSpinProjection.CaptureCurrent(
            targetRig.Body,
            sharedAxisX,
            targetCenter);

        try
        {
            await SoarSpinAnimation.PlayFiniteVerticalAxisProjection(
                ownerRig.Creature,
                duration,
                progress =>
                {
                    float degrees = GetTumbleAngleDegrees(progress);
                    ownerProjection.ApplyDegrees(degrees);
                    targetProjection.ApplyDegrees(degrees);
                });

            ownerProjection.ApplyDegrees(TotalTumbleDegrees);
            targetProjection.ApplyDegrees(TotalTumbleDegrees);
        }
        finally
        {
            ownerProjection.Restore();
            targetProjection.Restore();
        }
    }

    private static Vector2 ResolveOwnerSpinCenter(CreatureRig ownerRig)
    {
        Node2D? focus = NinjaSlayerVisualRig.GetCinematicFocus(ownerRig.Visuals);
        return focus?.GetGlobalTransformWithCanvas().Origin
            ?? ownerRig.Visuals.Bounds.GetGlobalRect().GetCenter();
    }

    internal static float GetTumbleAngleDegrees(float progress)
    {
        float p = Mathf.Clamp(progress, 0f, 1f);
        return TumbleAngleCoefficient * (p + 1.2f * p * p + 0.2f * p * p * p);
    }

    private static async Task TweenBodyScales(
        NCreature tweenOwner,
        BodyPivotCompensation ownerPivot,
        float ownerRotationDegrees,
        Vector2 ownerStartScale,
        Vector2 ownerTargetScale,
        BodyPivotCompensation targetPivot,
        float targetRotationDegrees,
        Vector2 targetStartScale,
        Vector2 targetTargetScale,
        float duration)
    {
        var tween = tweenOwner.CreateTween();
        tween.TweenMethod(
            Callable.From<float>(progress =>
            {
                ownerPivot.Apply(
                    ownerRotationDegrees,
                    ownerStartScale.Lerp(ownerTargetScale, progress));
                targetPivot.Apply(
                    targetRotationDegrees,
                    targetStartScale.Lerp(targetTargetScale, progress));
            }),
            0f,
            1f,
            duration)
            .SetTrans(Tween.TransitionType.Linear);

        await tweenOwner.ToSignal(tween, Tween.SignalName.Finished);
        ownerPivot.Apply(ownerRotationDegrees, ownerTargetScale);
        targetPivot.Apply(targetRotationDegrees, targetTargetScale);
    }

    private static void RestoreBodyTransform(Node2D body, CreatureVisualSnapshot snapshot)
    {
        if (!GodotObject.IsInstanceValid(body))
        {
            return;
        }

        body.Position = snapshot.BodyPosition;
        body.RotationDegrees = snapshot.BodyRotationDegrees;
        body.Scale = snapshot.BodyScale;
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

    private static async Task WaitTweenInterval(Node owner, float duration)
    {
        var tween = owner.CreateTween();
        tween.TweenInterval(duration);
        await owner.ToSignal(tween, Tween.SignalName.Finished);
    }

    private static async Task TweenBodyRotation(
        Node2D body,
        float targetDegrees,
        float duration,
        BodyPivotCompensation? pivotCompensation = null,
        Vector2? targetScale = null)
    {
        float startDegrees = body.RotationDegrees;
        Vector2 startScale = body.Scale;
        var tween = body.CreateTween();
        tween.TweenMethod(
                Callable.From<float>(progress =>
                {
                    float rotation = Mathf.Lerp(startDegrees, targetDegrees, progress);
                    Vector2 scale = targetScale.HasValue
                        ? startScale.Lerp(targetScale.Value, progress)
                        : body.Scale;
                    if (pivotCompensation is { } compensation)
                    {
                        compensation.Apply(rotation, scale);
                    }
                    else
                    {
                        body.RotationDegrees = rotation;
                        body.Scale = scale;
                    }
                }),
                0f,
                1f,
                duration)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Quad);

        await body.ToSignal(tween, Tween.SignalName.Finished);
        if (pivotCompensation is { } compensation)
        {
            compensation.Apply(targetDegrees, targetScale ?? body.Scale);
        }
        else
        {
            body.RotationDegrees = targetDegrees;
            if (targetScale.HasValue)
            {
                body.Scale = targetScale.Value;
            }
        }
    }

    private readonly record struct BodyPivotCompensation(
        Node2D Body,
        CanvasItem Parent,
        Vector2 MarkerBodyLocal,
        Vector2 MarkerParentLocal)
    {
        public static BodyPivotCompensation Capture(CreatureRig rig)
        {
            CanvasItem parent = rig.Body.GetParent<CanvasItem>();
            Vector2 markerCanvas = rig.Visuals.Bounds.GetGlobalRect().GetCenter();
            Vector2 markerBodyLocal = rig.Body.GetGlobalTransformWithCanvas().AffineInverse() * markerCanvas;
            Vector2 markerParentLocal = parent.GetGlobalTransformWithCanvas().AffineInverse() * markerCanvas;
            return new BodyPivotCompensation(rig.Body, parent, markerBodyLocal, markerParentLocal);
        }

        public void Apply(float rotationDegrees, Vector2 scale)
        {
            if (!GodotObject.IsInstanceValid(Body) || !GodotObject.IsInstanceValid(Parent))
            {
                return;
            }

            Body.RotationDegrees = rotationDegrees;
            Body.Scale = scale;
            Body.Position = MarkerParentLocal - Body.Transform.BasisXform(MarkerBodyLocal);
        }
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
