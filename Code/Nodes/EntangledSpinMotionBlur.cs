using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using NinjaSlayer.Code.ExternalAnimations;

namespace NinjaSlayer.Code.Nodes;

internal sealed partial class EntangledSpinMotionBlur : Node2D
{
    private readonly SpinExposureHistory _history = new();
    private readonly float[] _angles = new float[SpinExposureHistory.SampleCount];
    private Node2D _body = null!;
    private SubViewport _capture = null!;
    private Sprite2D _display = null!;
    private SpinExposureRenderer _renderer = null!;
    private VerticalAxisSpinProjection? _projection;
    private double _time;
    private float _ratio = 1f;
    private float _bodyHeight;
    private bool _active;

    internal static EntangledSpinMotionBlur Create(Node2D body, Rect2 fallbackBodyBounds)
    {
        Rect2 bounds = fallbackBodyBounds;
        if (body is Sprite2D sprite)
            bounds = sprite.GetRect();
        else if (body.IsClass(MegaSprite.spineClassName))
        {
            MegaSkeleton? skeleton = new MegaSprite(body).GetSkeleton();
            using GodotObject? skeletonLease = skeleton?.BoundObject;
            bounds = skeleton?.GetBounds() ?? fallbackBodyBounds;
        }
        if (!bounds.HasArea()) bounds = fallbackBodyBounds;
        var blur = new EntangledSpinMotionBlur
        {
            Name = "EntangledSpinBlur", _body = body, ProcessPriority = -1000,
            ZIndex = body.ZIndex, ZAsRelative = body.ZAsRelative,
            ShowBehindParent = body.ShowBehindParent, LightMask = body.LightMask
        };
        body.GetParent().AddChild(blur);
        body.GetParent().MoveChild(blur, body.GetIndex() + 1);
        try
        {
            blur.Initialize(bounds.Grow(8f));
            return blur;
        }
        catch
        {
            blur.RestoreDrawing();
            blur.QueueFree();
            throw;
        }
    }

    private void Initialize(Rect2 bounds)
    {
        float density = Math.Min(1f, 2048f / Math.Max(bounds.Size.X, bounds.Size.Y));
        _bodyHeight = bounds.Size.Y * density;
        _capture = new SubViewport
        {
            Name = "LiveBody", TransparentBg = true, Disable3D = true, GuiDisableInput = true,
            Size = new Vector2I((int)MathF.Ceiling(bounds.Size.X * density), (int)MathF.Ceiling(bounds.Size.Y * density)),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled
        };
        AddChild(_capture);
        _capture.CanvasTransform = new Transform2D(new Vector2(density, 0f),
            new Vector2(0f, density), -bounds.Position * density);
        _display = new Sprite2D
        {
            Name = "BodyExposure", Texture = _capture.GetTexture(), Centered = false,
            Position = bounds.Position, Scale = Vector2.One / density
        };
        AddChild(_display);
        _renderer = new SpinExposureRenderer(this);
        _body.TreeExiting += Stop;
        RenderingServer.FramePreDraw += SyncNow;
        _active = true;
        SyncNow();
    }

    public override void _Process(double delta)
    {
        if (_active && Engine.TimeScale > 0d) _time += delta / Engine.TimeScale;
    }

    internal void Record(VerticalAxisSpinProjection projection, float degrees, Func<double, double> before)
    {
        if (!_active || !CanProcess() || Engine.TimeScale <= 0d) return;
        _projection = projection;
        _ratio = VerticalSpinMath.GetScaleRatio(degrees);
        double timeScale = Engine.TimeScale;
        _history.Record(_time, degrees, age => before(age * timeScale));
    }

    internal void SyncNow()
    {
        if (!_active || !IsInsideTree()) return;
        if (!GodotObject.IsInstanceValid(_body) || !_body.IsInsideTree()) { Stop(); return; }

        // Redirect drawing only. The original Spine node keeps its parent,
        // animation state, skin, materials and gameplay references throughout.
        Transform = _body.Transform;
        TopLevel = _body.TopLevel;
        Visible = _body.Visible;
        RenderingServer.CanvasItemSetParent(_body.GetCanvasItem(), _capture.FindWorld2D().Canvas);
        RenderingServer.CanvasItemSetTransform(_body.GetCanvasItem(), Transform2D.Identity);
        if (!CanProcess() || Engine.TimeScale <= 0d) return;
        _capture.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        if (_projection != null && _history.SampleAngles(_time, _angles))
            _renderer.Apply(_display, _projection.AxisInSprite(_display).X, _ratio, _angles,
                _bodyHeight, premultiplied: true);
        else
            _renderer.Reset();
    }

    internal void Stop()
    {
        if (!_active) return;
        RestoreDrawing();
        if (IsInsideTree() && !IsQueuedForDeletion()) QueueFree();
    }

    private void RestoreDrawing()
    {
        if (!_active) return;
        _active = false;
        RenderingServer.FramePreDraw -= SyncNow;
        if (GodotObject.IsInstanceValid(_body))
        {
            _body.TreeExiting -= Stop;
            // Restore the native canvas link before freeing its temporary canvas.
            if (_body.IsInsideTree())
            {
                Rid parent = !_body.TopLevel && _body.GetParent() is CanvasItem item
                    ? item.GetCanvasItem() : _body.GetCanvas();
                RenderingServer.CanvasItemSetParent(_body.GetCanvasItem(), parent);
                RenderingServer.CanvasItemSetTransform(_body.GetCanvasItem(),
                    _body.TopLevel ? _body.GlobalTransform : _body.Transform);
            }
        }
        Visible = false;
        _capture.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        _renderer.Reset();
        _history.Clear();
    }

    public override void _ExitTree()
    {
        RestoreDrawing();
        _renderer?.Dispose();
    }
}
