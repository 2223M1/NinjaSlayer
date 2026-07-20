using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;
using MegaCrit.Sts2.Core.Saves;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.ExternalAnimations;

/// <summary>Owns the combat scene transform while a short cinematic is active.</summary>
public sealed class CombatCinematicCameraLease : IDisposable
{
    private static CombatCinematicCameraLease? _active;

    private readonly NCombatRoom _room;
    private readonly Control _sceneContainer;
    private readonly List<CameraSample> _followSamples = [];
    private bool _disposed;
    private bool _screenShakeTargetSuspended;
    private bool _layoutAdjustmentActive;
    private Vector2 _layoutCameraCenter;
    private Vector2 _cameraPosition;
    private float _cameraScale;
    private Vector2 _shakeOffset;
    private ScreenPunchInstance? _screenPunch;
    private int _responsiveLayoutAdjustmentCount;

    private CombatCinematicCameraLease(NCombatRoom room, string ownerName)
    {
        _room = room;
        _sceneContainer = room.SceneContainer;
        OwnerName = ownerName;
        BaselinePosition = _sceneContainer.Position;
        BaselineScale = _sceneContainer.Scale;
        _cameraPosition = BaselinePosition;
        _cameraScale = BaselineScale.X;
        ViewportSize = room.GetViewportRect().Size;

        NGame? game = NGame.Instance;
        if (game != null && ReferenceEquals(game.ScreenshakeTarget, _sceneContainer))
        {
            _screenShakeTargetSuspended = true;
            game.ClearScreenShakeTarget();
            Entry.Logger.Info($"Suspended combat screen shake target for {OwnerName} camera control.");
        }
    }

    public string OwnerName { get; }
    public Vector2 BaselinePosition { get; }
    public Vector2 BaselineScale { get; }
    public Vector2 ViewportSize { get; private set; }
    public float CurrentScale => _cameraScale;
    public Vector2 CurrentPosition => _cameraPosition;

    public static bool TryAcquire(NCombatRoom room, string ownerName, out CombatCinematicCameraLease? lease)
    {
        if (_active != null && !_active._disposed)
        {
            lease = null;
            return false;
        }

        lease = new CombatCinematicCameraLease(room, ownerName);
        _active = lease;
        return true;
    }

    internal static bool TryBeginResponsiveLayoutAdjustment(NCombatRoom room) =>
        _active?.BeginResponsiveLayoutAdjustment(room) == true;

    internal static void CompleteResponsiveLayoutAdjustment(NCombatRoom room) =>
        _active?.EndResponsiveLayoutAdjustment(room);

    public static bool TryRouteScreenShake(ShakeStrength strength, ShakeDuration duration, float degrees)
    {
        if (_active == null || _active._disposed)
        {
            return false;
        }

        _active.PlayScreenShake(strength, duration, degrees);
        return true;
    }

    public static float EaseOutCubic(float value)
    {
        float remaining = 1f - Mathf.Clamp(value, 0f, 1f);
        return 1f - remaining * remaining * remaining;
    }

    public void Advance(float delta)
    {
        if (_screenPunch == null || delta <= 0f)
        {
            return;
        }

        _shakeOffset = _screenPunch.Update(delta);
        if (_screenPunch.IsDone)
        {
            _screenPunch = null;
            _shakeOffset = Vector2.Zero;
        }

        ApplyTransform();
    }

    public void PlayScreenShake(ShakeStrength strength, ShakeDuration duration, float degrees = -1f)
    {
        float multiplier = NScreenshakePaginator.GetShakeMultiplier(
            SaveManager.Instance.PrefsSave.ScreenShakeOptionIndex);
        float scaledStrength = GetShakeStrength(strength) * multiplier;
        if (scaledStrength <= 0f)
        {
            _screenPunch = null;
            _shakeOffset = Vector2.Zero;
            ApplyTransform();
            return;
        }

        float angle = degrees < 0f
            ? MegaCrit.Sts2.Core.Random.Rng.Chaotic.NextFloat(360f)
            : degrees;
        _screenPunch = new ScreenPunchInstance(scaledStrength, GetShakeDuration(duration), angle);
        _shakeOffset = Vector2.Zero;
        ApplyTransform();
    }

    public Vector2 GetLocalCenter(CanvasItem target)
    {
        Vector2 globalCenter = target switch
        {
            Control control => control.GetGlobalRect().GetCenter(),
            Node2D node2D => node2D.GlobalPosition,
            _ => Vector2.Zero
        };
        return _sceneContainer.GetGlobalTransformWithCanvas().AffineInverse() * globalCenter;
    }

    public Vector2 GetCameraPosition(Vector2 localTarget, float scale, Vector2 screenTarget)
    {
        Vector2 pivot = _sceneContainer.PivotOffset;
        return screenTarget - pivot - (localTarget - pivot) * scale;
    }

    public Vector2 GetCameraCenter(Vector2 cameraPosition, float scale, Vector2 screenTarget)
    {
        Vector2 pivot = _sceneContainer.PivotOffset;
        return pivot + (screenTarget - pivot - cameraPosition) / scale;
    }

    public Vector2 ClampTarget(Vector2 localTarget, float scale)
    {
        if (scale <= 0f)
        {
            return _sceneContainer.Size * 0.5f;
        }

        Vector2 halfViewport = ViewportSize / (2f * scale);
        Vector2 maximum = _sceneContainer.Size - halfViewport;
        return new Vector2(
            halfViewport.X <= maximum.X
                ? Mathf.Clamp(localTarget.X, halfViewport.X, maximum.X)
                : _sceneContainer.Size.X * 0.5f,
            halfViewport.Y <= maximum.Y
                ? Mathf.Clamp(localTarget.Y, halfViewport.Y, maximum.Y)
                : _sceneContainer.Size.Y * 0.5f);
    }

    public void FrameOnLocalPoint(Vector2 localTarget, float scale)
    {
        SetTransform(GetCameraPosition(localTarget, scale, ViewportSize * 0.5f), scale);
    }

    public void FrameOn(CanvasItem target, float scale, bool clamp = false)
    {
        Vector2 center = GetLocalCenter(target);
        FrameOnLocalPoint(clamp ? ClampTarget(center, scale) : center, scale);
    }

    public void BeginDelayedFollow(CanvasItem target)
    {
        _followSamples.Clear();
        _followSamples.Add(new CameraSample(0f, GetLocalCenter(target)));
    }

    public void FrameOnDelayed(CanvasItem target, float scale, float elapsed, float delay)
    {
        AddFollowSample(elapsed, GetLocalCenter(target));
        float sampleTime = Math.Max(0f, elapsed - delay);
        while (_followSamples.Count > 2 && _followSamples[1].Time <= sampleTime)
        {
            _followSamples.RemoveAt(0);
        }

        FrameOnLocalPoint(ClampTarget(SampleFollowPosition(sampleTime), scale), scale);
    }

    public void SetTransform(Vector2 position, float scale)
    {
        _cameraPosition = position;
        _cameraScale = scale;
        ApplyTransform();
    }

    public void ResetToBaseline()
    {
        _screenPunch = null;
        _shakeOffset = Vector2.Zero;
        _cameraPosition = BaselinePosition;
        _cameraScale = BaselineScale.X;
        ApplyTransform();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ResetToBaseline();
        if (ReferenceEquals(_active, this))
        {
            _active = null;
        }

        if (_responsiveLayoutAdjustmentCount > 0)
        {
            Entry.Logger.Info(
                $"Protected {_responsiveLayoutAdjustmentCount} responsive combat layout adjustment(s) during {OwnerName}.");
        }

        RestoreScreenShakeTarget();
    }

    private bool BeginResponsiveLayoutAdjustment(NCombatRoom room)
    {
        if (_disposed
            || _layoutAdjustmentActive
            || !ReferenceEquals(room, _room)
            || !GodotObject.IsInstanceValid(_sceneContainer))
        {
            return false;
        }

        _layoutCameraCenter = GetCameraCenter(_cameraPosition, _cameraScale, ViewportSize * 0.5f);
        _sceneContainer.Position = BaselinePosition;
        _sceneContainer.Scale = BaselineScale;
        _layoutAdjustmentActive = true;
        return true;
    }

    private void EndResponsiveLayoutAdjustment(NCombatRoom room)
    {
        if (!_layoutAdjustmentActive || !ReferenceEquals(room, _room))
        {
            return;
        }

        _layoutAdjustmentActive = false;
        ViewportSize = _room.GetViewportRect().Size;
        _cameraPosition = GetCameraPosition(_layoutCameraCenter, _cameraScale, ViewportSize * 0.5f);
        ApplyTransform();
        _responsiveLayoutAdjustmentCount++;
    }

    private void RestoreScreenShakeTarget()
    {
        if (!_screenShakeTargetSuspended)
        {
            return;
        }

        _screenShakeTargetSuspended = false;
        if (!GodotObject.IsInstanceValid(_room)
            || !GodotObject.IsInstanceValid(_sceneContainer)
            || !_room.IsInsideTree()
            || !ReferenceEquals(NCombatRoom.Instance, _room))
        {
            Entry.Logger.Warn($"Skipped restoring {OwnerName} screen shake target because the combat room changed.");
            return;
        }

        NGame? game = NGame.Instance;
        if (game == null)
        {
            return;
        }

        Control? currentTarget = game.ScreenshakeTarget;
        if (currentTarget != null && !ReferenceEquals(currentTarget, _sceneContainer))
        {
            Entry.Logger.Warn($"Skipped restoring {OwnerName} screen shake target because another target took ownership.");
            return;
        }

        if (!ReferenceEquals(currentTarget, _sceneContainer))
        {
            game.SetScreenShakeTarget(_sceneContainer);
        }

        Entry.Logger.Info($"Restored combat screen shake target after {OwnerName} camera reset.");
    }

    private void ApplyTransform()
    {
        if (!GodotObject.IsInstanceValid(_sceneContainer))
        {
            return;
        }

        _sceneContainer.Scale = Vector2.One * _cameraScale;
        _sceneContainer.Position = _cameraPosition + _shakeOffset;
    }

    private void AddFollowSample(float elapsed, Vector2 position)
    {
        if (_followSamples.Count == 0)
        {
            _followSamples.Add(new CameraSample(elapsed, position));
            return;
        }

        CameraSample last = _followSamples[^1];
        if (elapsed <= last.Time + 0.0001f)
        {
            _followSamples[^1] = new CameraSample(last.Time, position);
            return;
        }

        _followSamples.Add(new CameraSample(elapsed, position));
    }

    private Vector2 SampleFollowPosition(float sampleTime)
    {
        if (_followSamples.Count == 0)
        {
            return Vector2.Zero;
        }

        CameraSample first = _followSamples[0];
        if (_followSamples.Count == 1 || sampleTime <= first.Time)
        {
            return first.Position;
        }

        CameraSample second = _followSamples[1];
        float duration = second.Time - first.Time;
        if (duration <= 0.0001f)
        {
            return second.Position;
        }

        return first.Position.Lerp(
            second.Position,
            Mathf.Clamp((sampleTime - first.Time) / duration, 0f, 1f));
    }

    private static float GetShakeStrength(ShakeStrength strength) => strength switch
    {
        ShakeStrength.VeryWeak => 2f,
        ShakeStrength.Weak => 5f,
        ShakeStrength.Medium => 20f,
        ShakeStrength.Strong => 40f,
        ShakeStrength.TooMuch => 80f,
        _ => 0f
    };

    private static double GetShakeDuration(ShakeDuration duration) => duration switch
    {
        ShakeDuration.Short => 0.3,
        ShakeDuration.Normal => 0.8,
        ShakeDuration.Long => 1.2,
        ShakeDuration.Forever => 999999999.0,
        _ => 0.0
    };

    private readonly record struct CameraSample(float Time, Vector2 Position);
}
