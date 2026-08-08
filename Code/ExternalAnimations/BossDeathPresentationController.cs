using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.ExternalAnimations;

public sealed partial class BossDeathPresentationController : Node
{
    private const float CameraScaleMultiplier = 2f;
    private const float CameraReturnSeconds = 0.2f;
    private const float SceneExitMargin = 96f;

    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private NCreature _boss = null!;
    private NCombatRoom _room = null!;
    private string _bossId = "unknown";
    private BossDeathPartSpec? _partSpec;
    private SpineBoneFlight? _partFlight;
    private BossDismembermentSnapshot? _dismembermentSnapshot;
    private CombatCinematicCameraLease? _camera;
    private readonly CinematicSessionLifetime _lifetime = new();
    private int _started;

    internal static BossDeathPresentationController Attach(
        NCreature boss,
        NCombatRoom room,
        BossDeathPartSpec? partSpec)
    {
        var controller = new BossDeathPresentationController
        {
            Name = "NinjaSlayerBossDeathPresentation",
            _boss = boss,
            _room = room,
            _bossId = boss.Entity.Monster?.Id.Entry ?? boss.Name.ToString(),
            _partSpec = partSpec
        };
        try
        {
            boss.AddChildSafely(controller);
            if (!GodotObject.IsInstanceValid(controller) || !controller.IsInsideTree())
            {
                throw new InvalidOperationException(
                    "The Boss death presentation controller could not enter the creature scene tree.");
            }

            return controller;
        }
        catch
        {
            if (GodotObject.IsInstanceValid(controller))
            {
                controller.QueueFreeSafely();
            }

            throw;
        }
    }

    internal float StartDeathAnimation(bool shouldRemove)
    {
        if (_dismembermentSnapshot == null)
        {
            throw new InvalidOperationException(
                "Boss death presentation started before its visual snapshot was prepared.");
        }

        GameCompatibility.CreaturePresentation.DisableInteractionForDeath(_boss);
        foreach (NIntent intent in _boss.IntentContainer.GetChildren().OfType<NIntent>())
        {
            intent.SetFrozen(isFrozen: true);
        }

        if (_boss.HasSpineAnimation)
        {
            MonsterModel? monster = _boss.Entity.Monster;
            if (monster is { HasDeathSfx: true })
            {
                SfxCmd.PlayDeath(monster);
            }

            _boss.SetAnimationTrigger("Dead");
        }

        if (!RunManager.Instance.IsSingleplayerOrFakeMultiplayer)
        {
            _boss.OrbManager?.ClearOrbs();
        }

        if (shouldRemove)
        {
            _boss.AnimHideIntent();
        }

        _boss.AnimDisableUi();
        Begin();
        Task deathTask = WaitForPresentationAndRemove(shouldRemove);
        _boss.DeathAnimationTask = deathTask;
        TaskHelper.RunSafely(deathTask);
        return BossBurstTimeline.LeadSeconds;
    }

    internal bool TryPrepareDeathAnimation()
    {
        if (_dismembermentSnapshot != null)
        {
            return true;
        }

        _dismembermentSnapshot = BossDismembermentPresentation.TryCapture(
            _room,
            _boss,
            _partSpec?.BoneName);
        return _dismembermentSnapshot != null;
    }

    private void Begin()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        if (_partSpec != null)
        {
            _partFlight = SpineBoneFlight.TryCreate(_boss, _partSpec.BoneName, _partSpec.MonsterId);
            if (_partFlight != null
                && CombatCinematicCameraLease.TryAcquire(
                    _room,
                    $"NinjaSlayer boss part flight ({_partSpec.MonsterId})",
                    out CombatCinematicCameraLease? camera))
            {
                _camera = camera;
            }
        }

        BossBurstRegistration registration = BossBurstPresentationCoordinator.Register(
            _room,
            new BossBurstParticipant(
                _boss.Entity.Monster?.Id.Entry ?? _boss.Name.ToString(),
                SpawnFragments));
        Task presentationTask = RunPresentation(registration, _lifetime.Token);
        TaskHelper.RunSafely(presentationTask);
    }

    public override void _ExitTree()
    {
        _lifetime.Dispose();
        DisposeDismembermentSnapshot();
        _partFlight?.Dispose();
        _camera?.Dispose();
        _completion.TrySetResult();
    }

    internal void AbortSetup()
    {
        _lifetime.Dispose();
        DisposeDismembermentSnapshot();
        _partFlight?.Dispose();
        _partFlight = null;
        _camera?.Dispose();
        _camera = null;
        _completion.TrySetResult();
        this.QueueFreeSafely();
    }

    private async Task WaitForPresentationAndRemove(bool shouldRemove)
    {
        await _completion.Task;
        if (shouldRemove && GodotObject.IsInstanceValid(_boss))
        {
            _boss.QueueFreeSafely();
        }
    }

    private async Task RunPresentation(
        BossBurstRegistration registration,
        CancellationToken cancelToken)
    {
        try
        {
            Task flightTask = _partFlight == null
                ? Task.CompletedTask
                : RunPartFlightUntilCue(_partFlight, registration.Cue, cancelToken);
            Task whiteoutTask = BossDeathWhiteoutLease.RunUntilCue(
                this,
                _room,
                _boss,
                _bossId,
                registration.Cue,
                cancelToken);
            await registration.Cue.WaitAsync(cancelToken);
            await Task.WhenAll(flightTask, whiteoutTask);
            await RestoreCamera(cancelToken);
            await registration.CombatRelease.WaitAsync(cancelToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Entry.Logger.Error(
                $"Boss death presentation failed for {_bossId}: {exception}");
        }
        finally
        {
            DisposeDismembermentSnapshot();
            _partFlight?.Dispose();
            _partFlight = null;
            _camera?.Dispose();
            _camera = null;
            _lifetime.Dispose();
            _completion.TrySetResult();
        }
    }

    private async Task RunPartFlightUntilCue(
        SpineBoneFlight flight,
        Task cue,
        CancellationToken cancelToken)
    {
        BossDeathPartSpec spec = _partSpec
            ?? throw new InvalidOperationException("Boss part flight started without a part specification.");
        Vector2 velocity = BossDeathPresentationConfig.GetVelocity(spec);
        float elapsed = 0f;
        while (!cancelToken.IsCancellationRequested
               && !cue.IsCompleted
               && elapsed < spec.MaximumFlightSeconds
               && IsRuntimeValid())
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            float delta = (float)GetProcessDeltaTime();
            if (delta <= 0f)
            {
                continue;
            }

            elapsed += delta;
            flight.Advance(velocity * delta, spec.RotationSpeedDegreesPerSecond * delta);
            Vector2 center = flight.GlobalCenter;
            if (_camera != null)
            {
                float scale = _camera.BaselineScale.X * CameraScaleMultiplier;
                _camera.FrameOnLocalPoint(ToSceneLocal(center), scale);
                _camera.Advance(delta);
            }

            if (IsOutsideScene(center))
            {
                break;
            }
        }
    }

    private BossDismembermentSpawn SpawnFragments()
    {
        BossDismembermentSnapshot? snapshot = _dismembermentSnapshot;
        _dismembermentSnapshot = null;
        try
        {
            if (!IsRuntimeValid())
            {
                return new BossDismembermentSpawn(false, Task.CompletedTask);
            }

            if (snapshot == null)
            {
                return BossDismembermentPresentation.CompleteWithoutFragments(
                    _boss,
                    "the pre-death visual snapshot is unavailable");
            }

            Vector2 bodyCenter = snapshot.BodyGlobalCenter;
            Vector2? partCenter = TryGetPartCenter(snapshot);
            BossDismembermentSpawn dismemberment = BossDismembermentPresentation.TrySpawn(
                _room,
                _boss,
                snapshot,
                bodyCenter,
                partCenter,
                BossBurstPresentationCoordinator.FragmentZIndex);
            if (dismemberment.Spawned)
            {
                TryHidePartFlight();
            }

            return dismemberment;
        }
        finally
        {
            snapshot?.Dispose();
        }
    }

    private Vector2? TryGetPartCenter(BossDismembermentSnapshot snapshot)
    {
        if (_partFlight == null)
        {
            return null;
        }

        try
        {
            Vector2 currentGlobalCenter = _partFlight.GlobalCenter;
            Vector2 sceneLocalCenter = _room.SceneContainer
                .GetGlobalTransform()
                .AffineInverse()
                * currentGlobalCenter;
            return snapshot.BaselineSceneToGlobal * sceneLocalCenter;
        }
        catch (Exception exception)
        {
            Entry.Logger.Warn(
                $"Boss part position became unavailable for {_bossId}; "
                + $"using the body burst origin instead: {exception.Message}");
            return null;
        }
    }

    private void TryHidePartFlight()
    {
        try
        {
            _partFlight?.MarkDisappeared();
        }
        catch (Exception exception)
        {
            Entry.Logger.Warn(
                $"Boss detached part could not be hidden for {_bossId}: {exception.Message}");
        }
    }

    private void DisposeDismembermentSnapshot()
    {
        BossDismembermentSnapshot? snapshot = _dismembermentSnapshot;
        _dismembermentSnapshot = null;
        snapshot?.Dispose();
    }

    private async Task RestoreCamera(CancellationToken cancelToken)
    {
        if (_camera == null || cancelToken.IsCancellationRequested)
        {
            return;
        }

        Vector2 startPosition = _camera.CurrentPosition;
        float startScale = _camera.CurrentScale;
        float elapsed = 0f;
        while (elapsed < CameraReturnSeconds && IsRuntimeValid())
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            float delta = (float)GetProcessDeltaTime();
            if (delta <= 0f)
            {
                continue;
            }

            elapsed += delta;
            float progress = CombatCinematicCameraLease.EaseOutCubic(elapsed / CameraReturnSeconds);
            _camera.SetTransform(
                startPosition.Lerp(_camera.BaselinePosition, progress),
                Mathf.Lerp(startScale, _camera.BaselineScale.X, progress));
            _camera.Advance(delta);
        }

        _camera.ResetToBaseline();
    }

    private bool IsRuntimeValid() =>
        GodotObject.IsInstanceValid(_boss)
        && GodotObject.IsInstanceValid(_room)
        && _boss.IsInsideTree()
        && ReferenceEquals(NCombatRoom.Instance, _room);

    private Vector2 ToSceneLocal(Vector2 globalPoint) =>
        _room.SceneContainer.GetGlobalTransformWithCanvas().AffineInverse() * globalPoint;

    private bool IsOutsideScene(Vector2 globalPoint)
    {
        Vector2 local = ToSceneLocal(globalPoint);
        Rect2 bounds = new(
            -Vector2.One * SceneExitMargin,
            _room.SceneContainer.Size + Vector2.One * SceneExitMargin * 2f);
        return !bounds.HasPoint(local);
    }
}
