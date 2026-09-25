using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Lifecycle;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.ExternalAnimations;

public static class AlabamaDropAnimation
{
    internal const string GroundContactName = "AlabamaGroundContact";
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
        bool visualTailOwnsRestore = false;
        bool poseTailOwnsRestore = false;
        Task impactResolutionTask = Task.CompletedTask;
        Node2D? targetBody = null;
        EntangledSpinMotionBlur? targetSpinBlur = null;
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
            targetSpinBlur?.Stop();
            MegaCrit.Sts2.Core.Audio.Debug.NDebugAudioManager.Instance?.Play(
                MegaCrit.Sts2.Core.Audio.Debug.TmpSfx.heavyAttack);
            SfxCmd.PlayDamage(target.Monster, 0);
            NCreature? targetNode = NCombatRoom.Instance?.GetCreatureNode(target);
            if (targetNode != null && !doomPoseFrozen)
            {
                DoomHurtPoseController.Resume(targetNode);
            }
            if (!doomPoseFrozen) await CreatureCmd.TriggerAnim(target, "Hit", 0f);
            if (targetBody != null && GodotObject.IsInstanceValid(targetBody))
            {
                targetBody.ProcessMode = Node.ProcessModeEnum.Disabled;
                targetBodyDisabled = true;
            }
            NinjaSlayerSpinMotionBlur.Get(owner)?.Reset();
            NinjaSlayerHellTornadoVisual.Get(owner)?.ClearExposure();
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

        using var freeControlLease = NinjaSlayerFreeControl.Get(owner)?.SuspendForCinematic(
            NinjaSlayerRapidAnimationCoordinator.GetBaseline(owner, ownerRig.CreatureNode));
        using var grabLayers = FinisherActorLayerLease.AcquireBehind(ownerRig.CreatureNode, targetRig.CreatureNode);
        targetBody = targetRig.Body;
        targetBodyProcessMode = targetRig.Body.ProcessMode;
        var ownerRestoreSnapshot = CreatureVisualSnapshot.Capture(ownerRig);
        bool rapid = RapidCardPresentationContext.IsActive && freeControlLease == null;
        if (rapid)
        {
            NinjaSlayerRapidAnimationCoordinator.PrepareAction(owner, ownerRig.CreatureNode);
        }

        Vector2 ownerAuthoredBaseline = rapid
            ? NinjaSlayerRapidAnimationCoordinator.GetBaseline(owner, ownerRig.CreatureNode)
            : ownerRig.CreatureNode.Position;
        var targetSnapshot = CreatureVisualSnapshot.Capture(targetRig);
        FinisherSession? finisher = FinisherSessionRegistry.GetActiveSession();
        bool architectRecovery = finisher?.Actor == owner && finisher.EventVisualPlay != null;
        Marker2D? groundContact = null;
        BodyPivotCompensation? landingPivot = null;
        Rect2 targetBodyBounds = targetRig.Body.GetGlobalTransformWithCanvas().AffineInverse()
            * targetRig.Visuals.Bounds.GetGlobalTransformWithCanvas()
            * new Rect2(Vector2.Zero, targetRig.Visuals.Bounds.Size);
        SoarSpinAnimation.SuspendForCinematic(owner);
        var ownerSnapshot = CreatureVisualSnapshot.Capture(ownerRig);
        BodyPivotCompensation ownerPivot = BodyPivotCompensation.Capture(ownerRig);
        BodyPivotCompensation targetPivot = BodyPivotCompensation.Capture(targetRig);
        Vector2 ownerStartPos = ownerAuthoredBaseline;
        Vector2 ownerLandingPos = ResolveOwnerLandingPosition(ownerRig, targetRig);
        NinjaSlayerAimPose? aimPose = NinjaSlayerAimPose.Get(owner);
        Vector2 ownerChargeScale = new(
            ownerSnapshot.BodyScale.X * LandingSquashScaleX,
            ownerSnapshot.BodyScale.Y * LandingSquashScaleY);
        Vector2 targetChargeScale = new(
            targetSnapshot.BodyScale.X * LandingSquashScaleX,
            targetSnapshot.BodyScale.Y * LandingSquashScaleY);
        Vector2 targetLandingScale = new(
            targetSnapshot.BodyScale.X * LandingSquashScaleX,
            targetSnapshot.BodyScale.Y * LandingSquashScaleY);
        float ownerInvertedRotation = ownerSnapshot.BodyRotationDegrees;
        Vector2 ownerInvertedScale = new(ownerSnapshot.BodyScale.X, -ownerSnapshot.BodyScale.Y);
        float targetInvertedRotation = targetSnapshot.BodyRotationDegrees + 180f;

        try
        {
            if (aimPose != null)
            {
                bool owned = NinjaSlayerFinisherCinematic.TryPlayOwnedAction(owner, LungeDuration, out Task approach);
                if (owned) await approach;
                else await NinjaSlayerRapidAnimationCoordinator.PlayAttackToPeak(owner,
                        NinjaSlayerCombatVisuals.AttackLungeDistance, LungeDuration,
                        FinisherActionTrajectory.FastProgress, standardPresentation: false);
                aimPose.BeginAction(target, exclusive: true);
                if (!owned) aimPose.PlaceAtImpact(target, ownerLandingPos.X, moveRoot: false);
            }
            else
            {
                await TweenHopNodePosition(ownerRig.CreatureNode, ownerRig.Visuals,
                    ownerRig.Visuals.Position + ownerLandingPos - ownerStartPos, LungeDuration, hopHeight: 0f);
            }

            PlayGrabFeedback(target);
            NGame.Instance?.ScreenShake(ShakeStrength.Weak, ShakeDuration.Short);
            doomPoseFrozen = DoomHurtPoseController.TryFreeze(targetRig.CreatureNode);
            await WaitTweenInterval(ownerRig.CreatureNode, GrabHoldDuration);

            Vector2 head = MeasureHeadContact(targetRig);
            groundContact = new Marker2D { Name = GroundContactName, Position = head };
            targetRig.Body.AddChild(groundContact);
            CanvasItem targetParent = targetRig.Body.GetParent<CanvasItem>();
            Vector2 floorCanvas = ((CanvasItem?)NinjaSlayerVisualRig.GetGroundContact(targetRig.Visuals)
                ?? targetRig.CreatureNode).GetGlobalTransformWithCanvas().Origin;
            Vector2 headCanvas = groundContact.GetGlobalTransformWithCanvas().Origin;
            Vector2 floor = targetParent.GetGlobalTransformWithCanvas().AffineInverse()
                * new Vector2(headCanvas.X, floorCanvas.Y);
            landingPivot = new(targetRig.Body, targetParent, head, floor);

            await Task.WhenAll(aimPose?.BlendToNeutral(CompressionDuration) ?? Task.CompletedTask, TweenBodyScales(
                ownerRig.CreatureNode,
                ownerPivot,
                ownerSnapshot.BodyRotationDegrees,
                ownerSnapshot.BodyScale,
                ownerChargeScale,
                targetPivot,
                targetSnapshot.BodyRotationDegrees,
                targetSnapshot.BodyScale,
                targetChargeScale,
                CompressionDuration));

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

            ownerPivot.Apply(ownerInvertedRotation, ownerInvertedScale);
            landingPivot.Value.Apply(targetInvertedRotation, targetSnapshot.BodyScale);

            targetSpinBlur = EntangledSpinMotionBlur.Create(targetRig.Body, targetBodyBounds);
            NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.NinjaSlayerLongWashoiEvent);
            await Task.WhenAll(
                ByrdFallAnimation.Play(owner, RiseDistance, FallDuration, playImpact: false),
                ByrdFallAnimation.Play(target, RiseDistance, FallDuration, playImpact: false),
                PlayEntangledFall(
                    ownerRig,
                    targetRig,
                    FallDuration,
                    targetSpinBlur));

            ownerPivot.Apply(ownerInvertedRotation, ownerInvertedScale);
            // The contact is the crown of the inverted head, not the standing
            // bounds center. A finisher owns its one downward compression.
            landingPivot.Value.Apply(targetInvertedRotation, targetSnapshot.BodyScale);
            // Finish both fall tracks and release their exposure before Doom can
            // freeze the actor. Otherwise the final spin Tween remains blurred.
            ByrdFallAnimation.PlayLandingImpact(null);
            await PlayImpact();
            finisher = FinisherSessionRegistry.GetActiveSession();
            bool finishing = finisher?.Actor == owner;
            if (!finishing)
                landingPivot.Value.Apply(targetInvertedRotation, targetLandingScale);
            else if (!architectRecovery)
            {
                ownerAuthoredBaseline = finisher!.ActorBaseline;
                ownerRestoreSnapshot = ownerRestoreSnapshot with { CreaturePosition = ownerAuthoredBaseline };
            }
            // The impact owns the final entangled pose until Doom has released.
            // Neither the session return nor an already-running hop may restore it.
            if (finishing) await finisher!.ImpactVisualCompletion;
            else await WaitTweenInterval(ownerRig.CreatureNode, LandingSquashHoldDuration);
            RestoreTargetBodyProcessMode();
            await impactResolutionTask;
            impactResolutionJoined = true;

            var visualTail = new AlabamaDropVisualTail(
                ownerRestoreSnapshot,
                targetSnapshot,
                ownerAuthoredBaseline);
            Task ownerRecovery;
            if (rapid && aimPose != null)
            {
                Transform2D impactBody = ownerRig.Body.GetGlobalTransformWithCanvas();
                NinjaSlayerRapidAnimationCoordinator.CancelOrdinaryActions(owner);
                ownerRestoreSnapshot.Restore(restoreNinjaSlayerAirborneState: true);
                aimPose.SyncNow();
                Transform2D restoredBody = ownerRig.Body.GetGlobalTransformWithCanvas();
                var recovery = aimPose.RecoverAlabama(impactBody, restoredBody, StandUpDuration, ReturnHopHeight);
                visualTail.TransferOwner(recovery);
                poseTailOwnsRestore = true;
                ownerRecovery = recovery.Completion;
            }
            else ownerRecovery = Task.WhenAll(
                architectRecovery ? finisher!.ReturnArchitectAlabama()
                    : TweenHopNodePosition(ownerRig.CreatureNode, ownerRig.Visuals,
                        ownerRestoreSnapshot.VisualsPosition, StandUpDuration, visualTail.Track, visualTail.SetProgress),
                TweenBodyRotation(ownerRig.Body, ownerSnapshot.BodyRotationDegrees + 360f,
                    StandUpDuration, ownerPivot, ownerSnapshot.BodyScale, visualTail.Track));
            Task standUpTask = Task.WhenAll(
                ownerRecovery,
                target.IsDead ? Task.CompletedTask : TweenBodyRotation(
                    targetRig.Body,
                    targetSnapshot.BodyRotationDegrees + 360f,
                    StandUpDuration,
                    targetPivot,
                    targetSnapshot.BodyScale,
                    visualTail.Track));
            if (rapid)
            {
                long tailGeneration = NinjaSlayerRapidAnimationCoordinator.RegisterReturnTail(
                    owner,
                    visualTail.TryTakeover,
                    visualTail.CancelAndRestore,
                    independentAirChannel: poseTailOwnsRestore);
                visualTailOwnsRestore = true;
                _ = TaskHelper.RunSafely(CompleteVisualTail(
                    owner,
                    tailGeneration,
                    standUpTask,
                    visualTail));
            }
            else
            {
                await standUpTask;
            }
        }
        finally
        {
            groundContact?.QueueFreeSafely();
            targetSpinBlur?.Stop();
            if (!poseTailOwnsRestore) aimPose?.Reset();
            RestoreTargetBodyProcessMode();
            if (impactResolutionStarted && !impactResolutionJoined)
            {
                _ = TaskHelper.RunSafely(impactResolutionTask);
            }

            if (doomPoseFrozen)
            {
                DoomHurtPoseController.Resume(targetRig.CreatureNode);
            }

            if (!visualTailOwnsRestore)
            {
                targetSnapshot.Restore(restoreNinjaSlayerAirborneState: false);
                ownerRestoreSnapshot.Restore(restoreNinjaSlayerAirborneState: true,
                    restoreCreaturePosition: !architectRecovery);
                if (!architectRecovery && GodotObject.IsInstanceValid(ownerRig.CreatureNode))
                {
                    ownerRig.CreatureNode.Position = ownerAuthoredBaseline;
                }
            }
        }
    }

    private static Vector2 MeasureHeadContact(CreatureRig rig)
    {
        Rect2 bounds = rig.Body.GetGlobalTransformWithCanvas().AffineInverse()
            * rig.Visuals.Bounds.GetGlobalTransformWithCanvas()
            * new Rect2(Vector2.Zero, rig.Visuals.Bounds.Size);
        if (rig.Body.IsClass(MegaSprite.spineClassName))
        {
            MegaSkeleton skeleton = new MegaSprite(rig.Body).GetSkeleton()!;
            using GodotObject skeletonLease = skeleton.BoundObject;
            var slots = skeleton.BoundObject.Call("get_slots").AsGodotArray<GodotObject>();
            var hidden = new List<(GodotObject Slot, Variant Attachment)>();
            try
            {
                bounds = skeleton.GetBounds();
                foreach (GodotObject slot in slots)
                {
                    using GodotObject data = slot.Call("get_data").AsGodotObject();
                    string name = data.Call("get_slot_name").AsString();
                    if (name.Contains("head", StringComparison.OrdinalIgnoreCase)) continue;
                    hidden.Add((slot, slot.Call("get_attachment")));
                    slot.Call("set_attachment", default(Variant));
                }
                Rect2 head = skeleton.GetBounds();
                // Some creatures have a single body attachment with no head slot.
                if (head.HasArea()) bounds = head;
            }
            finally
            {
                foreach (var (slot, attachment) in hidden)
                {
                    slot.Call("set_attachment", attachment);
                    attachment.Dispose();
                }
                foreach (GodotObject slot in slots) slot.Dispose();
            }
        }
        return new Vector2(bounds.GetCenter().X, bounds.Position.Y);
    }

    private static async Task CompleteVisualTail(
        Creature owner,
        long generation,
        Task standUpTask,
        AlabamaDropVisualTail visualTail)
    {
        try
        {
            await standUpTask;
        }
        finally
        {
            visualTail.RestoreAfterCompletion();
            NinjaSlayerRapidAnimationCoordinator.CompleteVisualTail(owner, generation);
        }
    }

    private static void PlayImpactFireBurst(Creature target)
    {
        try
        {
            NFireBurstVfx? vfx = NFireBurstVfx.Create(target, 0.75f);
            if (vfx is not null && NCombatRoom.Instance is { } room)
            {
                foreach (CanvasItem flame in vfx.GetNode<Node2D>("center_pivot").GetChildren()
                    .OfType<CanvasItem>().Where(child => child.Name.ToString().StartsWith("vfx_fire_burst_", StringComparison.Ordinal)))
                    flame.ZIndex = 0;
                room.BackCombatVfxContainer.AddChildSafely(vfx);
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
                room.CombatVfxContainer.AddChildSafely(hitSpark);
            }
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"Failed to play Alabama Drop grab VFX: {ex}");
        }

        SfxCmd.PlayDamage(target.Monster, 0);
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
        float duration,
        EntangledSpinMotionBlur targetBlur)
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
                    double AngleBefore(double age) => GetTumbleAngleDegrees(
                        duration > 0f ? Mathf.Max(0f, progress - (float)age / duration) : progress);
                    ownerProjection.ApplyDegrees(degrees, AngleBefore);
                    targetProjection.ApplyDegrees(degrees);
                    targetBlur.Record(targetProjection, degrees, AngleBefore);
                });

            ownerProjection.ApplyDegrees(TotalTumbleDegrees);
            targetProjection.ApplyDegrees(TotalTumbleDegrees);
        }
        finally
        {
            targetBlur.Stop();
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

        if (!await TweenPlayback.AwaitCompletion(tween, tweenOwner))
        {
            return;
        }
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

    private static async Task TweenHopNodePosition(
        NCreature creatureNode,
        Node2D motionNode,
        Vector2 target,
        float duration,
        Action<Tween>? onTweenCreated = null,
        Action<float>? onProgress = null,
        float hopHeight = ReturnHopHeight)
    {
        Vector2 start = FinisherApproach.AnimationPosition(creatureNode.Entity, motionNode);
        NinjaSlayerShadowController.Get(creatureNode.Entity)?.TrackRootHop(motionNode, target.Y);
        var tween = creatureNode.CreateTween();
        onTweenCreated?.Invoke(tween);
        tween.TweenMethod(
            Callable.From<float>(progress =>
            {
                onProgress?.Invoke(progress);
                float easedProgress = progress * progress * (3f - 2f * progress);
                float yOffset = Mathf.Sin(progress * Mathf.Pi) * hopHeight;
                Vector2 pos = start.Lerp(target, easedProgress);
                FinisherApproach.SetAnimationPosition(creatureNode.Entity, motionNode, new Vector2(pos.X, pos.Y - yOffset));
            }),
            0f,
            1f,
            duration)
            .SetTrans(Tween.TransitionType.Linear);

        if (!await TweenPlayback.AwaitCompletion(tween, creatureNode))
        {
            return;
        }
        FinisherApproach.SetAnimationPosition(creatureNode.Entity, motionNode, target);
    }

    private static async Task WaitTweenInterval(Node owner, float duration)
    {
        var tween = owner.CreateTween();
        tween.TweenInterval(duration);
        await TweenPlayback.AwaitCompletion(tween, owner);
    }

    private static async Task TweenBodyRotation(
        Node2D body,
        float targetDegrees,
        float duration,
        BodyPivotCompensation? pivotCompensation = null,
        Vector2? targetScale = null,
        Action<Tween>? onTweenCreated = null)
    {
        float startDegrees = body.RotationDegrees;
        Vector2 startScale = body.Scale;
        if (startScale.Y < 0f)
        {
            startDegrees += 180f;
            startScale = -startScale;
        }
        var tween = body.CreateTween();
        onTweenCreated?.Invoke(tween);
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

        if (!await TweenPlayback.AwaitCompletion(tween, body))
        {
            return;
        }
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

    private sealed class AlabamaDropVisualTail(
        CreatureVisualSnapshot ownerSnapshot,
        CreatureVisualSnapshot targetSnapshot,
        Vector2 ownerAuthoredBaseline)
    {
        private readonly List<Tween> _tweens = [];
        private bool _cancelled;
        private float _progress;
        private bool _ownerTransferred;
        private NinjaSlayerAimPose.VisualMotion? _ownerMotion;

        public void TransferOwner(NinjaSlayerAimPose.VisualMotion motion)
        {
            _ownerTransferred = true;
            _ownerMotion = motion;
        }

        public void SetProgress(float progress) => _progress = progress;

        public void Track(Tween tween)
        {
            _tweens.Add(tween);
            if (_cancelled && tween.IsValid())
            {
                tween.Kill();
            }
        }

        public void CancelAndRestore()
        {
            if (_cancelled)
            {
                return;
            }

            _cancelled = true;
            _ownerMotion?.Dispose();
            foreach (Tween tween in _tweens)
            {
                if (GodotObject.IsInstanceValid(tween) && tween.IsValid())
                {
                    tween.Kill();
                }
            }

            RestoreSnapshots();
        }

        public RapidMotionHandoff? TryTakeover()
        {
            if (_cancelled)
            {
                return null;
            }

            _cancelled = true;
            _ownerMotion?.Dispose();
            Vector2 currentPosition = ownerSnapshot.Visuals.Position;
            foreach (Tween tween in _tweens)
            {
                if (GodotObject.IsInstanceValid(tween) && tween.IsValid())
                {
                    tween.Kill();
                }
            }

            RestoreSnapshots(currentPosition);
            return new(
                [RapidMotionChannel.For(ownerSnapshot.Visuals, ownerSnapshot.VisualsPosition)],
                RapidAttackTrajectory.RemainingReturnSeconds(StandUpDuration, _progress));
        }

        public void RestoreAfterCompletion()
        {
            if (!_cancelled)
            {
                RestoreSnapshots();
            }
        }

        private void RestoreSnapshots(Vector2? ownerPosition = null)
        {
            targetSnapshot.Restore(restoreNinjaSlayerAirborneState: false);
            if (_ownerTransferred) return;
            ownerSnapshot.Restore(restoreNinjaSlayerAirborneState: true);
            if (GodotObject.IsInstanceValid(ownerSnapshot.CreatureNode))
            {
                ownerSnapshot.CreatureNode.Position = ownerAuthoredBaseline;
                FinisherApproach.SetAnimationPosition(ownerSnapshot.Creature, ownerSnapshot.Visuals,
                    ownerPosition ?? ownerSnapshot.VisualsPosition);
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
            Vector2 markerCanvas = NinjaSlayerAimPose.Get(rig.Creature)?.CoreCanvas
                ?? rig.Visuals.Bounds.GetGlobalRect().GetCenter();
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

            // Keep reflection on Y so the idle vertical-spin pivot does not
            // reinterpret this planar return as a mirrored column rotation.
            if (scale.X < 0f)
            {
                rotationDegrees += 180f;
                scale = -scale;
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
                FinisherApproach.AnimationPosition(rig.Creature, rig.Visuals),
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

        public void Restore(bool restoreNinjaSlayerAirborneState, bool restoreCreaturePosition = true)
        {
            if (!restoreNinjaSlayerAirborneState && Creature.IsDead) return;
            if (GodotObject.IsInstanceValid(CreatureNode))
            {
                if (restoreCreaturePosition) CreatureNode.Position = CreaturePosition;
                CreatureNode.RotationDegrees = CreatureRotationDegrees;
                CreatureNode.Scale = CreatureScale;
            }

            if (GodotObject.IsInstanceValid(Visuals))
            {
                FinisherApproach.SetAnimationPosition(Creature, Visuals, VisualsPosition);
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
