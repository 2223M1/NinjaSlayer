using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.Nodes;

internal sealed partial class NinjaSlayerHellTornadoVisual : Node2D
{
    internal const float TurnSeconds = 4f * 1001f / 24000f;
    private const string ResourceRoot = "res://NinjaSlayer/images/characters/ninja_slayer/hell_tornado/";
    private readonly Dictionary<string, Layers> _forms = [];
    private readonly Dictionary<VerticalAxisSpinProjection, float> _actionTurns = [];
    private NCreature _actor = null!;
    private Sprite2D _source = null!, _head = null!, _torso = null!;
    private NarakuVisualOverlay _overlay = null!;
    private FreeControlMotionBlur _blur = null!;
    private bool _active;
    private double _time;
    private double _angle;
    private double _rampStart;
    private double _rampTravel;
    private float _speed;
    private float _initialSpeed;
    private float _rampElapsed;
    private float _rampSeconds;
    private bool _accelerating;
    private TaskCompletionSource? _transition;
    private Transform2D _bodyCanvas;
    private uint _sourceVisibilityLayer;
    private uint _overlayVisibilityLayer;

    internal bool Active => _active;
    internal Transform2D BodyCanvasTransform => _bodyCanvas;
    internal static NinjaSlayerHellTornadoVisual? Get(Creature creature) =>
        NinjaSlayerAimPose.Get(creature)?.HellTornado;

    internal static NinjaSlayerHellTornadoVisual Create(NCreature actor, NinjaSlayerAimPose pose)
    {
        var node = new NinjaSlayerHellTornadoVisual { Name = "HellTornadoBody", _actor = actor, ProcessPriority = 100 };
        pose.AddChild(node);
        return node;
    }

    public override void _Ready()
    {
        _source = NinjaSlayerVisualRig.GetBodySprite(_actor.Visuals)!;
        _overlay = GetParent().GetNode<NarakuVisualOverlay>("NarakuVisualOverlay");
        _torso = new Sprite2D { Name = "OrbitingBody" };
        _head = new Sprite2D { Name = "FixedHead", ZIndex = 2 };
        _blur = new FreeControlMotionBlur { Name = "BodyExposure", ShowBehindParent = true };
        AddChild(_torso); AddChild(_head); AddChild(_blur);
        Hide();
        RenderingServer.FramePreDraw += SyncNow;
    }

    internal Task Accelerate(float seconds)
    {
        if (SoarSpinAnimation.IsSpinning(_actor.Entity) || SoarSpinAnimation.IsVerticalSpinActive(_actor.Entity))
            SoarSpinAnimation.ResetSpinVisual(_actor.Entity);
        if (!_active)
        {
            _sourceVisibilityLayer = _source.VisibilityLayer;
            _overlayVisibilityLayer = _overlay.VisibilityLayer;
        }
        _active = true;
        _source.VisibilityLayer = _overlay.VisibilityLayer = 0;
        NinjaSlayerRapidAnimationCoordinator.EnsureLifecycle(_actor.Entity);
        Show();
        return Ramp(seconds, accelerating: true);
    }

    internal Task Decelerate(float seconds) => Ramp(seconds, accelerating: false);

    internal void SetActionTurn(VerticalAxisSpinProjection owner, float degrees) => _actionTurns[owner] = degrees;
    internal void RemoveActionTurn(VerticalAxisSpinProjection owner) => _actionTurns.Remove(owner);

    private Task Ramp(float seconds, bool accelerating)
    {
        _transition?.TrySetResult();
        _transition = new TaskCompletionSource();
        _accelerating = accelerating;
        _rampSeconds = seconds;
        _rampElapsed = 0f;
        _rampStart = _angle;
        _initialSpeed = _speed;
        // End on the authored orientation so removing the layers cannot pop the body.
        _rampTravel = Math.Ceiling((_angle + _speed * seconds * .5d) / Mathf.Tau) * Mathf.Tau - _angle;
        if (seconds <= 0f)
        {
            _speed = accelerating ? Mathf.Tau / TurnSeconds : 0f;
            if (!accelerating) _angle = 0d;
            _transition.TrySetResult();
            SyncNow();
        }
        return _transition.Task;
    }

    public override void _Process(double delta)
    {
        if (!_active) return;
        if (_actor.Entity.IsDead) { Reset(); return; }
        _time += Engine.TimeScale > 0d ? delta / Engine.TimeScale : 0d;
        double step = delta;
        if (_transition is { Task.IsCompleted: false })
        {
            float used = Math.Min((float)step, _rampSeconds - _rampElapsed);
            _rampElapsed += used;
            step -= used;
            float p = Mathf.Clamp(_rampElapsed / _rampSeconds, 0f, 1f);
            if (_accelerating)
            {
                float maximum = Mathf.Tau / TurnSeconds;
                _angle = _rampStart + _rampSeconds * (_initialSpeed * p + (maximum - _initialSpeed) * p * p * p / 3f);
                _speed = Mathf.Lerp(_initialSpeed, maximum, p * p);
            }
            else
            {
                double tangent = _initialSpeed * _rampSeconds;
                _angle = _rampStart + (p * p * p - 2f * p * p + p) * tangent
                    + (-2f * p * p * p + 3f * p * p) * _rampTravel;
                _speed = (float)(((3f * p * p - 4f * p + 1f) * tangent
                    + (-6f * p * p + 6f * p) * _rampTravel) / _rampSeconds);
            }
            if (p >= 1f) _transition.TrySetResult();
        }
        _angle += _speed * step;
        SyncNow();
    }

    internal void SyncNow()
    {
        if (!_active || !IsInsideTree() || !GodotObject.IsInstanceValid(_source)) return;
        NinjaSlayerFormKind form = NinjaSlayerFormState.GetPresentation(_actor.Entity).Kind;
        string key = form switch
        {
            NinjaSlayerFormKind.Normal => NinjaSlayerFacingState.ResolveFacingLeft(_actor) ? "normal_left" : "normal",
            NinjaSlayerFormKind.Naraku => "semi_naraku",
            NinjaSlayerFormKind.FullyReleasedNaraku => "full_naraku",
            NinjaSlayerFormKind.OneBodyOneSoul => "one_soul",
            _ => throw new ArgumentOutOfRangeException(nameof(form))
        };
        Layers layers = LoadLayers(key);
        Sprite2D logical = _overlay.Visible ? _overlay : _source;
        Transform2D map = new(0f, logical.Offset);
        map *= new Transform2D(new(logical.FlipH ? -1f : 1f, 0f), new(0f, logical.FlipV ? -1f : 1f), Vector2.Zero);
        map *= new Transform2D(0f, logical.Centered ? -logical.Texture.GetSize() * .5f : Vector2.Zero);
        map *= layers.ToSource * new Transform2D(0f, layers.Body.GetSize() * .5f);
        Transform2D authored = logical.Transform * map;
        Vector2 pivot = authored * (layers.Pivot - layers.Body.GetSize() * .5f);
        double actionAngle = _actionTurns.Values.Sum(degrees => Mathf.DegToRad(degrees));
        Transform2D orbit = new((float)((_angle + actionAngle) % Mathf.Tau), pivot);
        orbit *= new Transform2D(0f, -pivot);
        _head.Texture = layers.Head;
        _torso.Texture = layers.Body;
        _head.Transform = authored;
        _torso.Transform = orbit * authored;
        _head.Modulate = _torso.Modulate = logical.Modulate * logical.SelfModulate;
        _bodyCanvas = GetParent<CanvasItem>().GetGlobalTransformWithCanvas() * orbit * logical.Transform;
        if (CanProcess() && Engine.TimeScale > 0d)
            _blur.RecordHistory(_torso, _time, GetGlobalTransformWithCanvas(), pivot, authored, _angle + actionAngle);
    }

    private Layers LoadLayers(string key)
    {
        if (_forms.TryGetValue(key, out Layers? cached)) return cached;
        using JsonDocument document = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(ResourceRoot + "layers.json"));
        JsonElement item = document.RootElement.GetProperty(key);
        float[] m = item.GetProperty("to_source").EnumerateArray().Select(e => e.GetSingle()).ToArray();
        JsonElement pivot = item.GetProperty("pivot");
        Texture2D head = GD.Load<Texture2D>(ResourceRoot + item.GetProperty("head").GetString());
        Texture2D body = GD.Load<Texture2D>(ResourceRoot + item.GetProperty("body").GetString());
        if (m.Length != 6 || head.GetSize() != body.GetSize())
            throw new InvalidOperationException($"Invalid separated-body resource: {key}.");
        var layers = new Layers(head, body, new(pivot[0].GetSingle(), pivot[1].GetSingle()),
            new(new(m[0], m[3]), new(m[1], m[4]), new(m[2], m[5])));
        _forms.Add(key, layers);
        return layers;
    }

    internal void Reset()
    {
        bool wasActive = _active;
        _active = false;
        _transition?.TrySetResult();
        _transition = null;
        _speed = 0f;
        _angle = 0d;
        _actionTurns.Clear();
        _blur?.ClearHistory();
        Hide();
        if (wasActive)
        {
            if (GodotObject.IsInstanceValid(_source)) _source.VisibilityLayer = _sourceVisibilityLayer;
            if (GodotObject.IsInstanceValid(_overlay)) _overlay.VisibilityLayer = _overlayVisibilityLayer;
        }
    }

    public override void _ExitTree()
    {
        RenderingServer.FramePreDraw -= SyncNow;
        Reset();
    }

    private sealed record Layers(Texture2D Head, Texture2D Body, Vector2 Pivot, Transform2D ToSource);
}
