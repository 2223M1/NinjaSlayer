using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using NinjaSlayer.Content;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.ExternalAnimations;

public sealed partial class BossDeathPresentationController : Node, IDeathDelayer
{
    private const float SoulLeadSeconds = 1f;
    private const float CameraScaleMultiplier = 2f;
    private const float CameraReturnSeconds = 0.2f;
    private const float SceneExitMargin = 96f;

    private static readonly Dictionary<ulong, BossDeathPresentationController> Active = [];

    private readonly TaskCompletionSource _completion = new();
    private NCreature _boss = null!;
    private NCombatRoom _room = null!;
    private BossDeathPartSpec? _partSpec;
    private SpineBoneFlight? _partFlight;
    private CombatCinematicCameraLease? _camera;
    private CancellationTokenSource? _cancelSource;
    private Task? _presentationTask;
    private float _estimatedDisappearSeconds;
    private bool _exploded;

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
        Active[boss.GetInstanceId()] = controller;
        return controller;
    }

    public static void NotifyDisappearanceStarted(NCreature boss)
    {
        if (Active.TryGetValue(boss.GetInstanceId(), out BossDeathPresentationController? controller))
        {
            controller.StartDisappearance();
        }
    }

    public void Begin(float estimatedDisappearSeconds)
    {
        if (_presentationTask != null)
        {
            return;
        }

        _estimatedDisappearSeconds = Math.Max(0f, estimatedDisappearSeconds);
        _cancelSource = new CancellationTokenSource();
        _presentationTask = RunPresentation(_cancelSource.Token);
        TaskHelper.RunSafely(_presentationTask);
    }

    public Task GetDelayTask() => _completion.Task;

    public override void _ExitTree()
    {
        Active.Remove(_boss?.GetInstanceId() ?? 0);
        _cancelSource?.Cancel();
        _partFlight?.Dispose();
        _camera?.Dispose();
        _completion.TrySetResult();
    }

    private async Task RunPresentation(CancellationToken cancelToken)
    {
        try
        {
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

            Task flightAndCameraTask = _partFlight == null
                ? Task.CompletedTask
                : RunPartFlightAndRestoreCamera(_partFlight, cancelToken);

            float soulDelay = Math.Max(0f, _estimatedDisappearSeconds - SoulLeadSeconds);
            if (soulDelay > 0f)
            {
                await Cmd.Wait(soulDelay, cancelToken, ignoreCombatEnd: true);
            }

            if (!cancelToken.IsCancellationRequested)
            {
                NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.NinjaSlayerNinjaSoulEvent);
            }

            float finalDelay = Math.Min(SoulLeadSeconds, _estimatedDisappearSeconds);
            if (finalDelay > 0f)
            {
                await Cmd.Wait(finalDelay, cancelToken, ignoreCombatEnd: true);
            }

            StartDisappearance();
            await flightAndCameraTask;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Entry.Logger.Error($"Boss death presentation failed for {_boss.Entity.Monster?.Id.Entry}: {exception}");
            StartDisappearance();
        }
        finally
        {
            _partFlight?.Dispose();
            _partFlight = null;
            _camera?.Dispose();
            _camera = null;
            _completion.TrySetResult();
        }
    }

    private async Task RunPartFlight(SpineBoneFlight flight, CancellationToken cancelToken)
    {
        BossDeathPartSpec spec = _partSpec
            ?? throw new InvalidOperationException("Boss part flight started without a part specification.");
        Vector2 velocity = BossDeathPresentationConfig.GetVelocity(spec);
        float elapsed = 0f;
        while (!cancelToken.IsCancellationRequested
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

        if (!cancelToken.IsCancellationRequested
            && elapsed >= spec.MaximumFlightSeconds
            && !IsOutsideScene(flight.GlobalCenter))
        {
            flight.MarkDisappeared();
        }
    }

    private async Task RunPartFlightAndRestoreCamera(
        SpineBoneFlight flight,
        CancellationToken cancelToken)
    {
        await RunPartFlight(flight, cancelToken);
        await RestoreCamera(cancelToken);
    }

    private void StartDisappearance()
    {
        if (_exploded || !IsRuntimeValid())
        {
            return;
        }

        _exploded = true;
        Vector2 center = _partFlight?.GlobalCenter ?? _boss.Visuals.Bounds.GetGlobalRect().GetCenter();
        SfxCmd.Play(BossDeathExplosionVfx.TemporaryExplosionSfx);
        BossDeathExplosionVfx.Play(_room, center);
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
        Rect2 bounds = new Rect2(-Vector2.One * SceneExitMargin,
            _room.SceneContainer.Size + Vector2.One * SceneExitMargin * 2f);
        return !bounds.HasPoint(local);
    }

}
