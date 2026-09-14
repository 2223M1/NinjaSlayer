using Godot;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace NinjaSlayer.Code.ExternalAnimations;

internal sealed class FinisherImpactPresentation : IDisposable
{
    private readonly CombatCinematicCameraLease _camera;
    private readonly ColorRect _backdrop;
    private bool _disposed;

    private FinisherImpactPresentation(NCombatRoom room, CombatCinematicCameraLease camera)
    {
        _camera = camera;
        _backdrop = new ColorRect
        {
            Name = "NinjaSlayerFinisherBackdrop",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Color = Colors.Transparent
        };
        try
        {
            Control background = room.GetNode<Control>("%BgContainer");
            room.SceneContainer.AddChild(_backdrop);
            _backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
            UpdateBackdropBounds(camera.BaselineVisibleSceneBounds);
            camera.BaselineVisibleSceneBoundsChanged += UpdateBackdropBounds;
            room.SceneContainer.MoveChild(_backdrop, background.GetIndex() + 1);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public static FinisherImpactPresentation CreateBackdropOnly(
        NCombatRoom room, CombatCinematicCameraLease camera) => new(room, camera);

    private void UpdateBackdropBounds(Rect2 bounds)
    {
        if (_disposed || !GodotObject.IsInstanceValid(_backdrop)) return;
        _backdrop.Position = bounds.Position;
        _backdrop.Size = bounds.Size;
    }

    public void SetBackdropIntensity(float intensity)
    {
        if (_disposed || !GodotObject.IsInstanceValid(_backdrop)) return;
        _backdrop.Color = new(.025f, .002f, .006f, .55f * Mathf.Clamp(intensity, 0f, 1f));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _camera.BaselineVisibleSceneBoundsChanged -= UpdateBackdropBounds;
        if (GodotObject.IsInstanceValid(_backdrop)) _backdrop.QueueFree();
    }
}
