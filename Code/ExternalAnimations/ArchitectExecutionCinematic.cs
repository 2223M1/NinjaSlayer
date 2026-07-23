using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.ExternalAnimations;

public sealed partial class ArchitectExecutionCinematic : Node
{
    private const string ControllerName = "NinjaSlayerArchitectExecution";
    private const string ArchitectHeadBone = "head";
    private const float InitialPauseSeconds = 0.5f;
    private const float FacingPauseSeconds = 0.5f;
    private const float FacingTurnSeconds = 0.15f;
    private const float ChargeSeconds = 0.2f;
    private const float ImpactSeconds = 0.3f;
    private const float ImpactPunchSeconds = 0.04f;
    private const float ImpactRecoveryStartSeconds = 0.2f;
    private const float HeadFlightSeconds = 1f;
    private const float HeadExplosionScreenMargin = 72f;
    private const float CameraScaleMultiplier = 2f;
    private const float ImpactScaleMultiplier = 2.12f;
    private const float CameraReturnSeconds = 0.2f;
    private const float ExitSpeedPixelsPerSecond = 420f;
    private const float ExitMargin = 160f;

    private TheArchitect _eventModel = null!;
    private Creature _owner = null!;
    private NCreature _ownerNode = null!;
    private NCreature _architectNode = null!;
    private NCombatRoom _room = null!;
    private CancellationTokenSource? _cancelSource;
    private CombatCinematicCameraLease? _camera;
    private FinisherImpactPresentation? _presentation;
    private SpineBoneFlight? _headFlight;
    private Vector2 _ownerStartPosition;
    private Vector2 _architectBodyPosition;
    private Vector2 _architectBodyScale;
    private float _architectBodyRotation;
    private Color _architectBodyModulate;
    private bool _doomFrozen;
    private bool _completed;
    private bool _headExploded;

    public static bool TryStart(TheArchitect eventModel)
    {
        Creature? owner = eventModel.Owner?.Creature;
        NCombatRoom? room = NCombatRoom.Instance;
        NCreature? ownerNode = room?.GetCreatureNode(owner);
        NCreature? architectNode = room?.CreatureNodes
            .FirstOrDefault(node => node.Entity.Monster is Architect);
        if (owner?.Player?.Character is not INinjaSlayerCharacter
            || room == null
            || ownerNode == null
            || architectNode == null
            || room.GetNodeOrNull(ControllerName) != null)
        {
            return false;
        }

        var controller = new ArchitectExecutionCinematic
        {
            Name = ControllerName,
            _eventModel = eventModel,
            _owner = owner,
            _ownerNode = ownerNode,
            _architectNode = architectNode,
            _room = room
        };
        room.AddChild(controller);
        controller.Begin();
        return true;
    }

    public override void _ExitTree()
    {
        _cancelSource?.Cancel();
        RestoreTemporaryState(restoreOwnerPosition: !_completed);
        _headFlight?.Dispose();
        _headFlight = null;
        _presentation?.Dispose();
        _presentation = null;
        _camera?.Dispose();
        _camera = null;
    }

    private void Begin()
    {
        _ownerStartPosition = _ownerNode.Position;
        _architectBodyPosition = _architectNode.Body.Position;
        _architectBodyScale = _architectNode.Body.Scale;
        _architectBodyRotation = _architectNode.Body.Rotation;
        _architectBodyModulate = _architectNode.Body.SelfModulate;
        _cancelSource = new CancellationTokenSource();
        TaskHelper.RunSafely(Run(_cancelSource.Token));
    }

    private async Task Run(CancellationToken cancelToken)
    {
        try
        {
            await WaitSeconds(InitialPauseSeconds, cancelToken);
            await TurnTo(faceLeft: true);
            await WaitSeconds(FacingPauseSeconds, cancelToken);
            await TurnTo(faceLeft: false);
            await WaitSeconds(FacingPauseSeconds, cancelToken);

            NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.NinjaSlayerKorosuBeshiEvent);
            PreparePresentation();
            await ChargeArchitect(cancelToken);
            await PlayImpact(cancelToken);
            await LaunchAndExplodeHead(cancelToken);
            await RestoreCameraAndBackdrop(cancelToken);
            await ExitScene(cancelToken);

            _completed = true;
            CompleteEvent();
        }
        catch (OperationCanceledException) when (cancelToken.IsCancellationRequested || !IsRuntimeValid())
        {
        }
        catch (Exception exception)
        {
            Entry.Logger.Error($"Architect execution cinematic failed: {exception}");
            RestoreTemporaryState(restoreOwnerPosition: true);
            if (IsRuntimeValid())
            {
                CompleteEvent();
            }
        }
        finally
        {
            RestoreTemporaryState(restoreOwnerPosition: !_completed);
            _presentation?.Dispose();
            _presentation = null;
            _camera?.Dispose();
            _camera = null;
        }
    }

    private async Task TurnTo(bool faceLeft)
    {
        await SoarSpinAnimation.PlayFiniteAirborneSpin(
            _owner,
            FacingTurnSeconds,
            progress => 180f * progress);
        NinjaSlayerFacingState.SetFacing(_ownerNode, faceLeft);
        SoarSpinAnimation.ResetSpinVisual(_owner);
    }

    private void PreparePresentation()
    {
        if (CombatCinematicCameraLease.TryAcquire(
                _room,
                "NinjaSlayer Architect execution",
                out CombatCinematicCameraLease? camera))
        {
            _camera = camera;
        }

        try
        {
            _presentation = FinisherImpactPresentation.Create(_room, 1);
        }
        catch (Exception exception)
        {
            Entry.Logger.Warn($"Architect execution backdrop unavailable: {exception}");
        }
    }

    private async Task ChargeArchitect(CancellationToken cancelToken)
    {
        NinjaSlayerCombatAudioSet.Play(NinjaSlayerCombatAudioSet.For(_owner).SlowAttack);
        Vector2 startPosition = _ownerNode.Position;
        Vector2 destination = ResolveApproachPosition(_ownerNode, _architectNode);
        Vector2 cameraStart = _camera?.CurrentPosition ?? Vector2.Zero;
        float elapsed = 0f;
        while (elapsed < ChargeSeconds)
        {
            elapsed += await NextFrame(cancelToken);
            float progress = Mathf.Clamp(elapsed / ChargeSeconds, 0f, 1f);
            float movementProgress = progress * progress;
            _ownerNode.Position = startPosition.Lerp(destination, movementProgress);
            _presentation?.SetBackdropIntensity(CombatCinematicCameraLease.EaseOutCubic(progress));
            FrameBothSubjects(cameraStart, progress);
        }

        _ownerNode.Position = destination;
        _presentation?.SetBackdropIntensity(1f);
        FrameBothSubjects(cameraStart, 1f);
    }

    private async Task PlayImpact(CancellationToken cancelToken)
    {
        Control vfxContainer = _room.CombatVfxContainer;
        int displayedDamage = Math.Max(1, ScoreUtility.CalculateScore(_owner.Player!.RunState, won: true));
        vfxContainer.AddChildSafely(NDamageNumVfx.Create(
            _architectNode.Entity,
            displayedDamage,
            requireInteractable: false));
        vfxContainer.AddChildSafely(NHitSparkVfx.Create(
            _architectNode.Entity,
            requireInteractable: false));
        VfxCmd.PlayOnCreatureCenter(_architectNode.Entity, "vfx/vfx_heavy_blunt");

        _doomFrozen = DoomHurtPoseController.TryFreeze(_architectNode);
        _architectNode.Body.Position = _architectBodyPosition;
        _architectNode.Body.Scale = _architectBodyScale * new Vector2(0.55f, 1.2f);
        _architectNode.Body.Rotation = _architectBodyRotation + Mathf.DegToRad(3f);
        _presentation?.SetImpactState([_architectNode], 1f, 1f);
        if (_camera != null)
        {
            _camera.PlayScreenShake(
                ShakeStrength.TooMuch,
                ShakeDuration.Short,
                rejectWeakerReplacement: true);
        }
        else
        {
            NGame.Instance?.ScreenShake(ShakeStrength.TooMuch, ShakeDuration.Short);
        }

        float elapsed = 0f;
        while (elapsed < ImpactSeconds)
        {
            float delta = await NextFrame(cancelToken);
            elapsed += delta;
            float scaleMultiplier = ResolveImpactScale(elapsed);
            FrameBothSubjectsAtScale(scaleMultiplier);
            _camera?.Advance(delta);

            float rays = elapsed < ImpactRecoveryStartSeconds
                ? 1f
                : 1f - Mathf.Clamp(
                    (elapsed - ImpactRecoveryStartSeconds)
                    / (ImpactSeconds - ImpactRecoveryStartSeconds),
                    0f,
                    1f);
            float flash = 1f - Mathf.Clamp(elapsed / ImpactPunchSeconds, 0f, 1f);
            _presentation?.SetImpactState([_architectNode], rays, flash);
        }

        _presentation?.SetImpactState([_architectNode], 0f, 0f);
        _architectNode.Body.Position = _architectBodyPosition;
        _architectNode.Body.Scale = _architectBodyScale;
        _architectNode.Body.Rotation = _architectBodyRotation;
        _architectNode.Body.SelfModulate = _architectBodyModulate;
    }

    private async Task LaunchAndExplodeHead(CancellationToken cancelToken)
    {
        _headFlight = SpineBoneFlight.TryCreate(
            _architectNode,
            ArchitectHeadBone,
            _architectNode.Entity.Monster?.Id.Entry ?? "ARCHITECT");
        NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.NinjaSlayerNinjaSoulEvent);
        if (_headFlight == null)
        {
            await WaitSeconds(HeadFlightSeconds, cancelToken);
            ExplodeAt(_architectNode.Visuals.Bounds.GetGlobalRect().GetCenter());
            return;
        }

        float cameraScale = GetCameraScale(CameraScaleMultiplier);
        Vector2 startGlobal = _headFlight.GlobalCenter;
        Vector2 startSceneLocal = ToSceneLocal(startGlobal);
        Vector2 targetSceneLocal = new(
            startSceneLocal.X,
            HeadExplosionScreenMargin / Mathf.Max(cameraScale, 0.0001f));
        Transform2D sceneToGlobal = _room.SceneContainer.GetGlobalTransformWithCanvas();
        Vector2 targetGlobal = sceneToGlobal * targetSceneLocal;
        Vector2 bodyStart = _architectNode.Body.ToLocal(startGlobal);
        Vector2 bodyTarget = _architectNode.Body.ToLocal(targetGlobal);
        Vector2 localOffset = bodyTarget - bodyStart;
        Vector2 cameraStart = _camera?.CurrentPosition ?? Vector2.Zero;

        float elapsed = 0f;
        while (elapsed < HeadFlightSeconds)
        {
            float delta = await NextFrame(cancelToken);
            elapsed += delta;
            float progress = Mathf.Clamp(elapsed / HeadFlightSeconds, 0f, 1f);
            _headFlight.SetRelativeTransform(localOffset * progress, 360f * progress);
            FollowHead(cameraStart, progress, cameraScale);
            _camera?.Advance(delta);
        }

        _headFlight.SetRelativeTransform(localOffset, 360f);
        FollowHead(cameraStart, 1f, cameraScale);
        Vector2 explosionCenter = _headFlight.GlobalCenter;
        _headFlight.MarkDisappeared();
        ExplodeAt(explosionCenter);
    }

    private void ExplodeAt(Vector2 globalCenter)
    {
        _headExploded = true;
        SfxCmd.Play(BossDeathExplosionVfx.TemporaryExplosionSfx);
        BossDeathExplosionVfx.Play(_room, globalCenter);
        if (_doomFrozen)
        {
            DoomHurtPoseController.Resume(_architectNode);
            _doomFrozen = false;
        }
    }

    private async Task RestoreCameraAndBackdrop(CancellationToken cancelToken)
    {
        if (_camera == null)
        {
            _presentation?.SetBackdropIntensity(0f);
            return;
        }

        Vector2 startPosition = _camera.CurrentPosition;
        float startScale = _camera.CurrentScale;
        float elapsed = 0f;
        while (elapsed < CameraReturnSeconds)
        {
            float delta = await NextFrame(cancelToken);
            elapsed += delta;
            float progress = CombatCinematicCameraLease.EaseOutCubic(
                elapsed / CameraReturnSeconds);
            _camera.SetTransform(
                startPosition.Lerp(_camera.BaselinePosition, progress),
                Mathf.Lerp(startScale, _camera.BaselineScale.X, progress));
            _camera.Advance(delta);
            _presentation?.SetBackdropIntensity(1f - progress);
        }

        _camera.ResetToBaseline();
        _presentation?.SetBackdropIntensity(0f);
    }

    private async Task ExitScene(CancellationToken cancelToken)
    {
        NinjaSlayerFacingState.SetFacing(_ownerNode, faceLeft: false);
        Vector2 start = _ownerNode.Position;
        float exitX = _room.SceneContainer.Size.X + ExitMargin;
        Vector2 destination = new(exitX, start.Y);
        float duration = Math.Max(0.1f, Mathf.Abs(destination.X - start.X) / ExitSpeedPixelsPerSecond);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += await NextFrame(cancelToken);
            _ownerNode.Position = start.Lerp(
                destination,
                Mathf.Clamp(elapsed / duration, 0f, 1f));
        }

        _ownerNode.Position = destination;
    }

    private void FrameBothSubjects(Vector2 cameraStart, float progress)
    {
        if (_camera == null)
        {
            return;
        }

        float scale = Mathf.Lerp(
            _camera.BaselineScale.X,
            GetCameraScale(CameraScaleMultiplier),
            CombatCinematicCameraLease.EaseOutCubic(progress));
        Vector2 targetPosition = ResolveDualSubjectCameraPosition(scale);
        _camera.SetTransform(
            cameraStart.Lerp(targetPosition, CombatCinematicCameraLease.EaseOutCubic(progress)),
            scale);
    }

    private void FrameBothSubjectsAtScale(float multiplier)
    {
        if (_camera == null)
        {
            return;
        }

        float scale = GetCameraScale(multiplier);
        _camera.SetTransform(ResolveDualSubjectCameraPosition(scale), scale);
    }

    private Vector2 ResolveDualSubjectCameraPosition(float scale)
    {
        Node2D? cinematicFocus = NinjaSlayerVisualRig.GetCinematicFocus(_ownerNode.Visuals);
        CanvasItem focus = cinematicFocus is not null ? cinematicFocus : _ownerNode;
        FinisherCameraFrame frame = FinisherCameraFraming.SelectTargets(
            _camera!,
            focus,
            [_architectNode],
            GetCameraScale(ImpactScaleMultiplier));
        Vector2 center = FinisherCameraFraming.ResolveCenter(
            _camera!,
            focus,
            frame,
            scale);
        return _camera!.GetCameraPosition(center, scale, _camera.ViewportSize * 0.5f);
    }

    private void FollowHead(Vector2 cameraStart, float progress, float scale)
    {
        if (_camera == null || _headFlight == null)
        {
            return;
        }

        Vector2 localCenter = ToSceneLocal(_headFlight.GlobalCenter);
        Vector2 clampedCenter = _camera.ClampTarget(localCenter, scale);
        Vector2 followPosition = _camera.GetCameraPosition(
            clampedCenter,
            scale,
            _camera.ViewportSize * 0.5f);
        float handoff = CombatCinematicCameraLease.EaseOutCubic(
            Mathf.Clamp(progress / 0.12f, 0f, 1f));
        _camera.SetTransform(cameraStart.Lerp(followPosition, handoff), scale);
    }

    private float ResolveImpactScale(float elapsed)
    {
        if (elapsed <= ImpactPunchSeconds)
        {
            float progress = CombatCinematicCameraLease.EaseOutCubic(
                elapsed / ImpactPunchSeconds);
            return Mathf.Lerp(CameraScaleMultiplier, ImpactScaleMultiplier, progress);
        }

        if (elapsed < ImpactRecoveryStartSeconds)
        {
            return ImpactScaleMultiplier;
        }

        float recovery = CombatCinematicCameraLease.EaseOutCubic(
            (elapsed - ImpactRecoveryStartSeconds)
            / (ImpactSeconds - ImpactRecoveryStartSeconds));
        return Mathf.Lerp(ImpactScaleMultiplier, CameraScaleMultiplier, recovery);
    }

    private float GetCameraScale(float multiplier) =>
        (_camera?.BaselineScale.X ?? 1f) * multiplier;

    private void RestoreTemporaryState(bool restoreOwnerPosition)
    {
        SoarSpinAnimation.ResetSpinVisual(_owner);
        if (restoreOwnerPosition && GodotObject.IsInstanceValid(_ownerNode))
        {
            _ownerNode.Position = _ownerStartPosition;
        }

        if (GodotObject.IsInstanceValid(_architectNode))
        {
            if (_doomFrozen)
            {
                DoomHurtPoseController.Resume(_architectNode);
                _doomFrozen = false;
            }

            _architectNode.Body.Position = _architectBodyPosition;
            _architectNode.Body.Scale = _architectBodyScale;
            _architectNode.Body.Rotation = _architectBodyRotation;
            _architectNode.Body.SelfModulate = _architectBodyModulate;
        }

        if (!_headExploded)
        {
            _headFlight?.Dispose();
            _headFlight = null;
        }
    }

    private void CompleteEvent()
    {
        if (_eventModel.Owner?.RunState.Players.Count > 1)
        {
            _room.SetWaitingForOtherPlayersOverlayVisible(visible: true);
        }

        RunManager.Instance.ActChangeSynchronizer.SetLocalPlayerReady();
    }

    private async Task WaitSeconds(float seconds, CancellationToken cancelToken)
    {
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            elapsed += await NextFrame(cancelToken);
        }
    }

    private async Task<float> NextFrame(CancellationToken cancelToken)
    {
        cancelToken.ThrowIfCancellationRequested();
        if (!IsRuntimeValid())
        {
            throw new OperationCanceledException("Architect execution room was unloaded.", cancelToken);
        }

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        cancelToken.ThrowIfCancellationRequested();
        return _room.ProcessMode == ProcessModeEnum.Disabled
            ? 0f
            : Math.Min((float)GetProcessDeltaTime(), 0.05f);
    }

    private bool IsRuntimeValid() =>
        GodotObject.IsInstanceValid(_room)
        && GodotObject.IsInstanceValid(_ownerNode)
        && GodotObject.IsInstanceValid(_architectNode)
        && _room.IsInsideTree()
        && ReferenceEquals(NCombatRoom.Instance, _room);

    private Vector2 ToSceneLocal(Vector2 globalPoint) =>
        _room.SceneContainer.GetGlobalTransformWithCanvas().AffineInverse() * globalPoint;

    private static Vector2 ResolveApproachPosition(NCreature owner, NCreature target)
    {
        float direction = Mathf.Sign(target.Position.X - owner.Position.X);
        if (Mathf.IsZeroApprox(direction))
        {
            direction = 1f;
        }

        float targetHalfWidth = target.Visuals.Bounds.Size.X
            * Mathf.Abs(target.Visuals.Scale.X)
            * 0.5f;
        return new Vector2(
            target.Position.X
            - direction * (targetHalfWidth + NinjaSlayerCombatVisuals.CloseRangeApproachGap),
            owner.Position.Y);
    }
}
