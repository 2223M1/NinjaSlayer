using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.ExternalAnimations;

public sealed partial class BossDeathPresentationController : Node
{
    private const float CameraScaleMultiplier = 2f;
    private const float CameraReturnSeconds = 0.2f;
    private const float SceneExitMargin = 96f;

    private readonly TaskCompletionSource _completion = new();
    private NCreature _boss = null!;
    private NCombatRoom _room = null!;
    private BossDeathPartSpec? _partSpec;
    private SpineBoneFlight? _partFlight;
    private BossDismembermentSnapshot? _dismembermentSnapshot;
    private CombatCinematicCameraLease? _camera;
    private CancellationTokenSource? _cancelSource;
    private Task? _presentationTask;

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
            _partSpec = partSpec
        };
        boss.AddChild(controller);
        return controller;
    }

    internal float StartDeathAnimation(bool shouldRemove)
    {
        _boss.DisableInteractionForDeath();
        foreach (NIntent intent in _boss.IntentContainer.GetChildren().OfType<NIntent>())
        {
            intent.SetFrozen(isFrozen: true);
        }

        _dismembermentSnapshot = BossDismembermentPresentation.TryCapture(_boss);

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

    private void Begin()
    {
        if (_presentationTask != null)
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
            new BossBurstParticipant(_boss, SpawnFragments));
        _cancelSource = new CancellationTokenSource();
        _presentationTask = RunPresentation(registration, _cancelSource.Token);
        TaskHelper.RunSafely(_presentationTask);
    }

    public override void _ExitTree()
    {
        _cancelSource?.Cancel();
        DisposeDismembermentSnapshot();
        _partFlight?.Dispose();
        _camera?.Dispose();
        _completion.TrySetResult();
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
            await registration.Cue.WaitAsync(cancelToken);
            await flightTask;
            await RestoreCamera(cancelToken);
            await registration.Completion.WaitAsync(cancelToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Entry.Logger.Error(
                $"Boss death presentation failed for {_boss.Entity.Monster?.Id.Entry}: {exception}");
        }
        finally
        {
            DisposeDismembermentSnapshot();
            _partFlight?.Dispose();
            _partFlight = null;
            _camera?.Dispose();
            _camera = null;
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
                return BossDismembermentPresentation.StartOriginalFadeFallback(_boss);
            }

            Vector2 bodyCenter = snapshot.BodyGlobalBounds.GetCenter();
            Vector2? partCenter = _partFlight?.GlobalCenter;
            BossDismembermentSpawn dismemberment = BossDismembermentPresentation.TrySpawn(
                _room,
                _boss,
                snapshot,
                bodyCenter,
                _partFlight == null ? null : _partSpec?.BoneName,
                partCenter,
                BossBurstPresentationCoordinator.FragmentZIndex);
            if (dismemberment.Spawned)
            {
                _partFlight?.MarkDisappeared();
            }

            return dismemberment;
        }
        finally
        {
            snapshot?.Dispose();
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
