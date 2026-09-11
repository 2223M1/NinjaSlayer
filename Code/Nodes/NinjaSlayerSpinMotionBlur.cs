using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Code.ExternalAnimations;

namespace NinjaSlayer.Code.Nodes;

[GlobalClass]
public partial class NinjaSlayerSpinMotionBlur : Node
{
    // All three authored bodies span 816 texture pixels, excluding scarf and smoke.
    private const float BodyHeight = 816f;
    private readonly SpinExposureHistory _history = new();
    private readonly float[] _angles = new float[SpinExposureHistory.SampleCount];
    private Sprite2D _body = null!;
    private NarakuVisualOverlay? _overlay;
    private Creature? _creature;
    private SpinExposureRenderer? _renderer;
    private VerticalAxisSpinProjection? _projection;
    private double _time;
    private float _ratio = 1f;
    private float _degrees;
    private float _angleBasis;
    private bool _hasExposure;
    private bool _projecting;
    private bool _subscribed;

    public override void _EnterTree()
    {
        if (IsNodeReady() && !_subscribed)
        {
            RenderingServer.FramePreDraw += SyncNow;
            _subscribed = true;
        }
    }

    public override void _Ready()
    {
        _body = GetParent().GetNode<Sprite2D>("%Visuals");
        _overlay = _body.GetParent().GetNodeOrNull<NarakuVisualOverlay>("NarakuVisualOverlay");
        for (Node? node = this; node != null; node = node.GetParent())
            if (node is NCreature actor) { _creature = actor.Entity; break; }
        // Advance only here, before animation writers. PreDraw never advances time.
        ProcessPriority = -1000;
        RenderingServer.FramePreDraw += SyncNow;
        _subscribed = true;
    }

    public override void _Process(double delta)
    {
        if (_creature?.IsDead == true) { Reset(); return; }
        if (!_body.CanProcess()) return;
        if (Engine.TimeScale > 0d) _time += delta / Engine.TimeScale;
    }

    internal void Record(VerticalAxisSpinProjection projection, float degrees,
        Func<double, double>? angleAtSecondsBefore, float angleBasis = 0f)
    {
        if (!IsInsideTree() || !CanProcess() || !_body.CanProcess() || Engine.TimeScale <= 0d) return;
        _projection = projection;
        _projecting = true;
        _degrees = degrees + angleBasis;
        _angleBasis = angleBasis;
        _ratio = VerticalSpinMath.GetScaleRatio(degrees);
        double timeScale = Engine.TimeScale;
        _history.Record(_time, _degrees, angleAtSecondsBefore == null ? null
            : age => angleAtSecondsBefore(age * timeScale));
        _hasExposure = true;
    }

    internal void Stop(VerticalAxisSpinProjection projection)
    {
        if (ReferenceEquals(_projection, projection))
        {
            _ratio = 1f;
            _projecting = false;
            _history.Record(_time, _angleBasis + 360d * Math.Round((_degrees - _angleBasis) / 360d), discontinuity: true);
        }
    }

    internal void ProjectVariant(Sprite2D sprite)
    {
        if (!_projecting || _projection == null) return;
        sprite.Scale = new Vector2(sprite.Scale.X / _ratio, sprite.Scale.Y);
        sprite.Rotation = _body.Rotation;
        _projection.ProjectVariant(sprite, _ratio);
    }

    internal void SyncNow()
    {
        if (!_hasExposure || !IsInsideTree() || !CanProcess() || !_body.CanProcess() || Engine.TimeScale <= 0d) return;
        if (_creature?.IsDead == true) { Reset(); return; }
        if (!_history.SampleAngles(_time, _angles)) { _renderer?.Reset(); return; }
        for (int i = 0; i < _angles.Length; i++) _angles[i] -= _angleBasis;
        _overlay?.SyncForPose();
        Sprite2D sprite = _overlay?.Visible == true ? _overlay : _body;
        _renderer ??= new SpinExposureRenderer(this);
        _renderer.Apply(sprite, _projection!.AxisInSprite(sprite).X, _ratio, _angles, BodyHeight);
    }

    public void Reset()
    {
        _renderer?.Reset();
        _history.Clear();
        _projection = null;
        _projecting = false;
        _hasExposure = false;
    }

    public override void _ExitTree()
    {
        if (_subscribed) RenderingServer.FramePreDraw -= SyncNow;
        _subscribed = false;
        Reset();
        _renderer?.Dispose();
        _renderer = null;
    }

    public static NinjaSlayerSpinMotionBlur? Get(Creature creature) =>
        creature.GetCreatureNode()?.Visuals.GetNodeOrNull<NinjaSlayerSpinMotionBlur>("SpinMotionBlur");
}
