using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Content;
using NVector2 = System.Numerics.Vector2;

namespace NinjaSlayer.Code.Nodes;

[GlobalClass]
public partial class NinjaSlayerShadowController : Node
{
    private Sprite2D _shadow = null!;
    private Sprite2D _body = null!;
    private Node2D _rig = null!;
    private string _rigName = null!;
    private Marker2D _groundAnchor = null!;
    private Transform2D _bodyBaseline;
    private Vector2 _groundPosition;
    private Vector2 _anchorPosition;
    private Rect2 _groundReferenceRect;
    private Color _groundModulate;
    private Vector2 _authoredScale;
    private bool _baselineFlipH;
    private bool _baselineFlipV;
    private bool _mirrored;
    private float _scaleMultiplier = 1f;
    private float _fall;
    private float _fallDirection;
    private object? _spinOwner;
    private float _spinRadians;
    private bool _syncing;
    private Vector2 _actionScale = Vector2.One;
    private Vector2 _actionStart = Vector2.One;
    private Vector2 _actionPeak = Vector2.One;
    private float _actionElapsed;
    private float _actionOut;
    private float _actionReturn;
    private bool _actionHeld;
    private NCreature? _hoppingRoot;
    private float _hopGroundY;
    private bool _rootHasLifted;

    internal static NinjaSlayerShadowController? Get(Creature creature) =>
        creature.GetCreatureNode()?.Visuals.GetNodeOrNull<NinjaSlayerShadowController>(NinjaSlayerVisualRig.ShadowControllerNodeName);

    public override void _Ready()
    {
        _rig = GetParent<Node2D>();
        _shadow = _rig.GetNode<Sprite2D>(NinjaSlayerVisualRig.ShadowNodeName);
        Node2D visuals = _rig.GetNode<Node2D>("%Visuals");
        _body = visuals as Sprite2D ?? visuals.GetNode<Sprite2D>("Sprite");
        string path = _body.Texture.ResourcePath;
        _rigName = path.Contains("/ninja_slayer/", StringComparison.Ordinal) ? "NinjaSlayer"
            : path.Contains("yamoto_koki", StringComparison.Ordinal) ? "YamotoKoki"
            : path.Contains("dark_ninja", StringComparison.Ordinal) ? "DarkNinja"
            : path.Contains("sawatari", StringComparison.Ordinal) ? "Sawatari"
            : path.Contains("yukano", StringComparison.Ordinal) ? "Yukano"
            : throw new InvalidOperationException($"No ground-shadow geometry for {path}.");
        _bodyBaseline = _rig.GlobalTransform.AffineInverse() * _body.GlobalTransform;
        _baselineFlipH = _body.FlipH;
        _baselineFlipV = _body.FlipV;
        _groundPosition = _shadow.Position;
        _authoredScale = _shadow.Scale.Abs();
        _groundModulate = _shadow.Modulate;
        _groundAnchor = _rig.GetNode<Marker2D>("GroundContact");
        _anchorPosition = _groundAnchor.Position;
        _groundReferenceRect = new(_groundAnchor.Position - _shadow.Texture.GetSize() * _authoredScale * 0.5f,
            _shadow.Texture.GetSize() * _authoredScale);
        ProcessPriority = 200;
        RenderingServer.FramePreDraw += SyncNow;
        SyncNow();
    }

    public override void _ExitTree() => RenderingServer.FramePreDraw -= SyncNow;

    public override void _Process(double delta)
    {
        if (_actionOut + _actionReturn > 0f)
        {
            _actionElapsed += (float)delta;
            if (_actionOut > 0f && _actionElapsed < _actionOut)
                _actionScale = _actionStart.Lerp(_actionPeak, Mathf.SmoothStep(0f, 1f, _actionElapsed / _actionOut));
            else if (_actionHeld) _actionScale = _actionPeak;
            else
            {
                float p = _actionReturn > 0f ? Mathf.Clamp((_actionElapsed - _actionOut) / _actionReturn, 0f, 1f) : 1f;
                _actionScale = _actionPeak.Lerp(Vector2.One, Mathf.SmoothStep(0f, 1f, p));
                if (p >= 1f) _actionOut = _actionReturn = 0f;
            }
        }
        SyncNow();
    }

    internal void BeginAction(ShadowActionKind kind, float outward, float recovery, bool hold = false)
    {
        NVector2 peak = GroundShadowMath.ActionScale(kind);
        _actionStart = _actionScale;
        _actionPeak = new(peak.X, peak.Y);
        _actionOut = Math.Max(0f, outward);
        _actionReturn = Math.Max(0f, recovery);
        _actionElapsed = 0f;
        _actionHeld = hold && outward > 0f;
        if (outward <= 0f) _actionScale = _actionPeak;
        if (outward <= 0f && recovery <= 0f) _actionScale = Vector2.One;
    }

    internal void BeginReturn(float seconds)
    {
        _actionPeak = _actionScale;
        _actionOut = _actionElapsed = 0f;
        _actionReturn = Math.Max(0f, seconds);
        _actionHeld = false;
        if (seconds <= 0f) _actionScale = Vector2.One;
    }

    internal void ResetAction()
    {
        _actionOut = _actionReturn = _actionElapsed = 0f;
        _actionHeld = false;
        _actionScale = Vector2.One;
        SyncNow();
    }

    internal void SetSpin(object owner, float radians) { _spinOwner = owner; _spinRadians = radians; }
    internal void ClearSpin(object owner) { if (ReferenceEquals(_spinOwner, owner)) { _spinOwner = null; _spinRadians = 0f; } }
    public void SetScaleMultiplier(float multiplier) { _scaleMultiplier = Mathf.Max(0f, multiplier); SyncNow(); }
    internal void SetAuthoredPresentation(Vector2 position, Vector2 scale)
    {
        _groundPosition = position;
        _authoredScale = scale.Abs();
        SyncNow();
    }
    internal void SetMirrored(bool mirrored)
    {
        _mirrored = mirrored;
        if (IsNodeReady())
            _groundAnchor.Position = new(_rigName == "YamotoKoki" || !mirrored ? _anchorPosition.X : -_anchorPosition.X, _anchorPosition.Y);
        SyncNow();
    }
    public void SetDeathFall(float progress, float direction)
    {
        _fall = Mathf.Clamp(progress, 0f, 1f);
        _fallDirection = direction < 0f ? -1f : 1f;
        SyncNow();
    }
    public void ClearDeathFall() { _fall = 0f; ResetAction(); }

    internal void TrackRootHop(NCreature root, float groundY)
    {
        _hoppingRoot = root;
        _hopGroundY = groundY;
        _rootHasLifted = false;
    }

    internal Rect2 GroundReferenceRect => _groundReferenceRect;

    internal void SyncNow()
    {
        if (_syncing || !IsNodeReady() || !GodotObject.IsInstanceValid(_body) || !GodotObject.IsInstanceValid(_groundAnchor)) return;
        _syncing = true;
        try
        {
            NinjaSlayerAimPose? aim = _rig.GetNodeOrNull<NinjaSlayerAimPose>("%AimPose");
            if (aim?.IsNodeReady() == true) aim.SyncNow();
            Sprite2D body = _rig.FindChild("NarakuVisualOverlay", true, false) is Sprite2D { Visible: true } overlay ? overlay : _body;
            string rigName = _rigName;
            bool full = body.Texture.ResourcePath == NinjaSlayerFormPresentationCatalog.FullyReleasedNarakuTexturePath;
            bool standing = body.Texture.ResourcePath.Contains("dark_ninja_standing", StringComparison.Ordinal);
            Vector2[] contour = ShadowBodyGeometry.Resolve(rigName, full, standing);
            Transform2D baseline = full ? new Transform2D(0f, new Vector2(0.5f, 0.5f), 0f, new Vector2(0f, -233f)) : _bodyBaseline;
            Transform2D current = _rig.GlobalTransform.AffineInverse() * body.GlobalTransform;
            bool normalized = ShadowBodyGeometry.UsesNormalizedPixels(rigName);
            Vector2 size = body.Texture.GetSize();
            Span<NVector2> initial = stackalloc NVector2[contour.Length];
            Span<NVector2> posed = stackalloc NVector2[contour.Length];
            for (int i = 0; i < contour.Length; i++)
            {
                Vector2 p = normalized ? (contour[i] - Vector2.One * 0.5f) * size : contour[i];
                Vector2 original = new(p.X * (_baselineFlipH ? -1f : 1f), p.Y * (_baselineFlipV ? -1f : 1f));
                Vector2 actual = new(p.X * (body.FlipH ? -1f : 1f), p.Y * (body.FlipV ? -1f : 1f));
                original = baseline * original;
                actual = current * (actual + body.Offset);
                initial[i] = new(original.X, original.Y);
                posed[i] = new(actual.X, actual.Y);
            }
            ShadowSupport reference = GroundShadowMath.Measure(initial);
            ShadowSupport support = GroundShadowMath.Measure(posed);
            Vector2 rootLift = Vector2.Zero;
            if (GodotObject.IsInstanceValid(_hoppingRoot))
            {
                float lift = _hopGroundY - _hoppingRoot!.Position.Y;
                _rootHasLifted |= !Mathf.IsZeroApprox(lift);
                rootLift = _rig.GetGlobalTransformWithCanvas().AffineInverse().BasisXform(
                    _hoppingRoot.GetParent<CanvasItem>().GetGlobalTransformWithCanvas().BasisXform(new Vector2(0f, lift)));
                if (_rootHasLifted && Mathf.IsZeroApprox(lift)) _hoppingRoot = null;
            }
            float mirrorAxis = rigName == "YamotoKoki" ? _groundPosition.X : 0f;
            float originalCenter = _mirrored ? 2f * mirrorAxis - reference.Center : reference.Center;
            float groundX = _mirrored ? 2f * mirrorAxis - _groundPosition.X : _groundPosition.X;
            float centerShift = support.Center - originalCenter;
            float width = Mathf.Clamp(support.Width / reference.Width, 0.6f, 2.5f);
            float tilt = MathF.Atan2(current.X.Y, current.X.X);
            if (_mirrored) tilt = GroundedPoseMath.WrapAngle(tilt - MathF.PI);
            float lean = MathF.Sin(tilt);
            if (_spinOwner != null)
            {
                // Billboard flattening must not collapse the ground footprint.
                width = GroundShadowMath.SpinWidth(_spinRadians);
            }
            var air = GroundShadowMath.Airborne(Math.Max(0f, reference.Bottom - support.Bottom + rootLift.Y), reference.Height);
            float depth = 1f + 0.18f * MathF.Abs(lean);
            float skew = lean * 0.12f;
            if (_spinOwner != null) { depth *= 1f + 0.18f * MathF.Abs(MathF.Sin(_spinRadians)); skew += MathF.Sin(2f * _spinRadians) * 0.10f; }
            if (_fall > 0f)
            {
                width = Math.Max(width, Mathf.Lerp(1f, 1.6093761f, _fall));
                depth = Math.Max(depth, Mathf.Lerp(1f, 1.7040393f, _fall));
                skew += _fallDirection * Mathf.DegToRad(34.2089f) * _fall;
            }
            _shadow.Position = new Vector2(groundX + centerShift, _groundPosition.Y) + rootLift;
            _shadow.Scale = _authoredScale * _scaleMultiplier * _actionScale * new Vector2(width * air.Width, depth * air.Depth);
            _shadow.Rotation = lean * 0.035f;
            _shadow.Skew = skew;
            _shadow.FlipH = _mirrored;
            _shadow.Modulate = new(_groundModulate.R, _groundModulate.G, _groundModulate.B, _groundModulate.A * air.Alpha);
        }
        finally { _syncing = false; }
    }
}
