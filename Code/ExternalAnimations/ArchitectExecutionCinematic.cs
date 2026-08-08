using Godot;
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
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.ExternalAnimations;

public sealed partial class ArchitectExecutionCinematic : Node
{
    private const string ControllerName = "NinjaSlayerArchitectExecution";
    private const float InitialPauseSeconds = 0.5f;
    private const float FacingPauseSeconds = 0.5f;
    private const float FacingTurnSeconds = 0.15f;
    private const float ChargeSeconds = 0.2f;
    private const float ImpactSeconds = 0.3f;
    private const float ImpactPunchSeconds = 0.04f;
    private const float ImpactRecoveryStartSeconds = 0.2f;
    private const float CameraScaleMultiplier = 2f;
    private const float ImpactScaleMultiplier = 2.12f;
    private const float CameraReturnSeconds = 0.2f;
    private const float ExitSpeedPixelsPerSecond = 840f;
    private const float ExitMargin = 160f;

    private Creature _owner = null!;
    private NCreature _ownerNode = null!;
    private NCreature _architectNode = null!;
    private NCombatRoom _room = null!;
    private readonly CinematicSessionLifetime _runLifetime = new();
    private CinematicSessionLifetime? _exitLifetime;
    private Task? _exitTask;
    private Task? _victoryCompletionTask;
    private CombatCinematicCameraLease? _camera;
    private FinisherImpactPresentation? _presentation;
    private BossDismembermentSnapshot? _dismembermentSnapshot;
    private ArchitectBossSoftBodyLead? _softBodyLead;
    private Vector2 _ownerStartPosition;
    private Vector2 _architectBodyPosition;
    private Vector2 _architectBodyScale;
    private float _architectBodyRotation;
    private Color _architectBodyModulate;
    private bool _doomFrozen;
    private bool _initialized;
    private bool _completed;
    private bool _architectDeathCommitted;
    private bool _architectVisualHidden;

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
            _owner = owner,
            _ownerNode = ownerNode,
            _architectNode = architectNode,
            _room = room
        };
        try
        {
            room.AddChildSafely(controller);
            if (!GodotObject.IsInstanceValid(controller) || !controller.IsInsideTree())
            {
                controller.QueueFreeSafely();
                return false;
            }

            controller.Begin();
            return true;
        }
        catch (Exception exception)
        {
            Entry.Logger.Error($"Architect execution setup failed: {exception}");
            controller.QueueFreeSafely();
            return false;
        }
    }

    public override void _ExitTree()
    {
        _runLifetime.Dispose();
        Interlocked.Exchange(ref _exitLifetime, null)?.Dispose();
        _softBodyLead?.Dispose();
        _softBodyLead = null;
        DisposeDismembermentSnapshot();
        HideArchitectVisual();
        if (_initialized)
        {
            RestoreTemporaryState(restoreOwnerPosition: _exitTask == null);
        }
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
        _initialized = true;
        _dismembermentSnapshot = BossDismembermentPresentation.TryCapture(
            _room,
            _architectNode);
        TaskHelper.RunSafely(Run(_runLifetime.Token));
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
            await PlayArchitectDeath(cancelToken);

            _completed = true;
            await CompleteEvent();
        }
        catch (OperationCanceledException) when (cancelToken.IsCancellationRequested || !IsRuntimeValid())
        {
        }
        catch (Exception exception)
        {
            Entry.Logger.Error($"Architect execution cinematic failed: {exception}");
            if (IsRuntimeValid())
            {
                await CompleteEvent();
            }
        }
        finally
        {
            if (!_completed)
            {
                _exitLifetime?.Cancel();
            }

            _softBodyLead?.Dispose();
            _softBodyLead = null;
            DisposeDismembermentSnapshot();
            HideArchitectVisual();
            RestoreTemporaryState(restoreOwnerPosition: _exitTask == null);
            _presentation?.Dispose();
            _presentation = null;
            _camera?.Dispose();
            _camera = null;
            _runLifetime.Dispose();
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
            try
            {
                _presentation = FinisherImpactPresentation.Create(_room, camera, 1);
            }
            catch (Exception exception)
            {
                Entry.Logger.Warn($"Architect execution backdrop unavailable: {exception}");
            }
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
        NinjaSlayerCombatVfx.PlayDefectStrikeHitFx(_architectNode.Entity);

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

        // Hoisted for the same reason as the enemy finisher impact loop.
        NCreature[] impactTargets = [_architectNode];
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
            _presentation?.SetImpactState(impactTargets, rays, flash);
        }

        _presentation?.SetImpactState(impactTargets, 0f, 0f);
        _architectNode.Body.Position = _architectBodyPosition;
        _architectNode.Body.Scale = _architectBodyScale;
        _architectNode.Body.Rotation = _architectBodyRotation;
        _architectNode.Body.SelfModulate = _architectBodyModulate;
    }

    private async Task PlayArchitectDeath(CancellationToken cancelToken)
    {
        float fallDirection = Mathf.Sign(_architectNode.Position.X - _ownerNode.Position.X);
        if (Mathf.IsZeroApprox(fallDirection))
        {
            fallDirection = 1f;
        }

        GameCompatibility.CreaturePresentation.DisableInteractionForDeath(_architectNode);
        _architectNode.AnimHideIntent();
        _architectNode.AnimDisableUi();
        _architectDeathCommitted = true;

        BossDismembermentSnapshot? snapshot = _dismembermentSnapshot;
        _dismembermentSnapshot = null;
        try
        {
            _softBodyLead = BossDismembermentPresentation.TrySpawnArchitectLead(
                _room,
                _architectNode,
                snapshot,
                fallDirection,
                BossBurstPresentationCoordinator.FragmentZIndex);
        }
        finally
        {
            snapshot?.Dispose();
        }

        string monsterId = _architectNode.Entity.Monster?.Id.Entry ?? "ARCHITECT";
        bool fragmentReplacementReady = _softBodyLead != null;
        BossBurstRegistration registration = BossBurstPresentationCoordinator.Register(
            _room,
            new BossBurstParticipant(
                monsterId,
                SpawnArchitectBurst));
        Task whiteout = BossDeathWhiteoutLease.RunUntilCue(
            this,
            _room,
            _architectNode,
            monsterId,
            registration.Cue,
            cancelToken);
        StartExitScene();
        Task cameraRestore = RestoreCameraAndBackdrop(cancelToken);

        await registration.Cue.WaitAsync(cancelToken);
        await Task.WhenAll(
            cameraRestore,
            whiteout,
            registration.CombatRelease.WaitAsync(cancelToken));
        if (!fragmentReplacementReady)
        {
            HideArchitectVisual();
        }
    }

    private BossDismembermentSpawn SpawnArchitectBurst()
    {
        ArchitectBossSoftBodyLead? lead = Interlocked.Exchange(ref _softBodyLead, null);
        return lead?.TriggerBurst()
            ?? new BossDismembermentSpawn(false, Task.CompletedTask);
    }

    private void StartExitScene()
    {
        if (_exitTask != null)
        {
            return;
        }

        var lifetime = new CinematicSessionLifetime();
        _exitLifetime = lifetime;
        _exitTask = RunExitScene(lifetime);
        TaskHelper.RunSafely(_exitTask);
    }

    private async Task RunExitScene(CinematicSessionLifetime lifetime)
    {
        try
        {
            await ExitScene(lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested || !IsRuntimeValid())
        {
        }
        catch (Exception exception)
        {
            Entry.Logger.Warn($"Architect exit movement ended early: {exception.Message}");
        }
        finally
        {
            Interlocked.CompareExchange(ref _exitLifetime, null, lifetime);
            lifetime.Dispose();
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
        if (_camera is not { } camera)
        {
            return;
        }

        float scale = Mathf.Lerp(
            camera.BaselineScale.X,
            GetCameraScale(CameraScaleMultiplier),
            CombatCinematicCameraLease.EaseOutCubic(progress));
        Vector2 targetPosition = ResolveDualSubjectCameraPosition(camera, scale);
        camera.SetTransform(
            cameraStart.Lerp(targetPosition, CombatCinematicCameraLease.EaseOutCubic(progress)),
            scale);
    }

    private void FrameBothSubjectsAtScale(float multiplier)
    {
        if (_camera is not { } camera)
        {
            return;
        }

        float scale = GetCameraScale(multiplier);
        camera.SetTransform(ResolveDualSubjectCameraPosition(camera, scale), scale);
    }

    private Vector2 ResolveDualSubjectCameraPosition(
        CombatCinematicCameraLease camera,
        float scale)
    {
        Node2D? cinematicFocus = NinjaSlayerVisualRig.GetCinematicFocus(_ownerNode.Visuals);
        CanvasItem focus = cinematicFocus is not null ? cinematicFocus : _ownerNode;
        FinisherCameraFrame frame = FinisherCameraFraming.SelectTargets(
            camera,
            focus,
            [_architectNode],
            GetCameraScale(ImpactScaleMultiplier));
        Vector2 center = FinisherCameraFraming.ResolveCenter(
            camera,
            focus,
            frame,
            scale);
        return camera.GetCameraPosition(center, scale, camera.ViewportSize * 0.5f);
    }

    private static float ResolveImpactScale(float elapsed)
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

        if (!_architectDeathCommitted && GodotObject.IsInstanceValid(_architectNode))
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

    }

    private void DisposeDismembermentSnapshot()
    {
        BossDismembermentSnapshot? snapshot = _dismembermentSnapshot;
        _dismembermentSnapshot = null;
        snapshot?.Dispose();
    }

    private Task CompleteEvent() => _victoryCompletionTask ??= CompleteEventCore();

    private async Task CompleteEventCore()
    {
        ArchitectVictoryCleanup.Mark(_owner);
        await GameCompatibility.ArchitectVictory.Complete(_owner.Player!, _room);
    }

    private void HideArchitectVisual()
    {
        if (!_architectDeathCommitted || _architectVisualHidden)
        {
            return;
        }

        _architectVisualHidden = true;
        if (GodotObject.IsInstanceValid(_architectNode)
            && GodotObject.IsInstanceValid(_architectNode.Body))
        {
            _architectNode.Body.Visible = false;
        }
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
        && (_architectDeathCommitted || GodotObject.IsInstanceValid(_architectNode))
        && _room.IsInsideTree()
        && ReferenceEquals(NCombatRoom.Instance, _room);

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
