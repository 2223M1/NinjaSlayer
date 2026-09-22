using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.Code.Nodes;

internal sealed partial class NativeStolenCardMotion : Node
{
    internal const string ScenePath = "res://scenes/creature_visuals/thieving_hopper.tscn";
    private const float ReturnSeconds = 50f / 30f;
    private Node2D _card = null!;
    private Control _scene = null!;
    private NCard _face = null!;
    private NCreatureVisuals _driver = null!;
    private Node2D _spine = null!;
    private Marker2D _marker = null!;
    private MegaTrackEntry _track = null!;
    private Transform2D _reference;
    private Transform2D _baseline;
    private float _duration;
    private float _elapsed;
    private float _sampledTime;
    private bool _death;
    private bool _stopped;
    private Node2D? _target;
    private Transform2D _heldReference;
    private Vector2 _sourceCenter;
    private Vector2 _sourceReference;
    private Vector2 _displayCenter;
    private bool _mirrored;
    private Action? _completed;
    private Func<Transform2D>? _follow;

    internal Vector2 HeldScale { get; private set; }

    internal static NativeStolenCardMotion Create(Node2D card, bool death, Action? completed = null,
        Func<Transform2D>? follow = null, Node2D? target = null, bool mirrored = false)
    {
        var motion = new NativeStolenCardMotion
        {
            ProcessMode = ProcessModeEnum.Pausable,
            _card = card, _death = death, _completed = completed, _baseline = card.GlobalTransform,
            _follow = follow, _target = target, _mirrored = mirrored,
            _scene = NCombatRoom.Instance!.SceneContainer
        };
        motion._scene.AddChildSafely(motion);
        return motion;
    }

    public override void _Ready()
    {
        _face = _card.GetChild<NCard>(0);
        _driver = PreloadManager.Cache.GetScene(ScenePath).Instantiate<NCreatureVisuals>();
        // Spine skips applying animation and updating bone followers when hidden.
        _driver.Modulate = new Color(1f, 1f, 1f, 0f);
        _driver.ProcessMode = ProcessModeEnum.Disabled;
        AddChild(_driver);
        _spine = _driver.GetCurrentBody();
        _marker = _driver.GetNode<Marker2D>("%StolenCardPos");
        var animationState = _driver.SpineBody!.GetAnimationState();
        animationState.SetAnimation("steal", false);
        _track = animationState.GetCurrent(0)!;
        _track.SetMixDuration(0f);
        Sample(_track.GetAnimationDuration());
        HeldScale = _marker.GlobalScale.Abs();
        _heldReference = _driver.GlobalTransform.AffineInverse() * _marker.GlobalTransform;
        if (_death)
        {
            _driver.Scale = new Vector2(_baseline.Determinant() < 0f ? -1f : 1f, 1f);
            animationState.SetAnimation("die", false);
            _track = animationState.GetCurrent(0)!;
            _track.SetMixDuration(0f);
            Sample(0f);
        }
        else
        {
            _sourceCenter = _scene.GetGlobalTransform().AffineInverse() * _target!.GlobalPosition;
            _driver.GlobalPosition = _card.GlobalPosition;
            Node2D targetBone = _driver.GetNode<Node2D>("Visuals/SpineBoneNode");
#if NINJASLAYER_CHANNEL_STABLE
            targetBone.Position = Vector2.Right * (_target.GlobalPosition.X - _driver.GlobalPosition.X);
#else
            targetBone.GlobalPosition = new Vector2(_target.GlobalPosition.X + 900f * _driver.GlobalScale.X,
                targetBone.GlobalPosition.Y);
#endif
            Sample(1.1f);
            _sourceReference = (_driver.GlobalTransform.AffineInverse() * _marker.GlobalTransform).Origin;
            // Native theft deals damage after its trigger and fixed reveal wait.
            // That time has already elapsed when our confirmed-hit hook runs.
            _elapsed = CombatActionTimingRuntime.TriggerSeconds(.25f) + .6f;
            _card.Visible = true;
            SfxCmd.Play("event:/sfx/enemy/enemy_attacks/thieving_hopper/thieving_hopper_steal");
        }
        _duration = _track.GetAnimationDuration();
        _reference = _marker.GlobalTransform;
        Sample(_elapsed);
        RenderingServer.FramePreDraw += ApplyPose;
    }

    private void Sample(float time)
    {
        _track.SetTrackTime(time);
        _spine.Call("update_skeleton", 0f);
    }

    public override void _Process(double delta)
    {
        if (_stopped || !GodotObject.IsInstanceValid(_card)) { Stop(); return; }
        _elapsed += (float)delta;
    }

    private void ApplyPose()
    {
        if (_stopped || !GodotObject.IsInstanceValid(_card)) { Stop(); return; }
        // Return tweens run after _Process. Resolve the hand and card together
        // just before drawing so the body's movement is not applied twice.
        _sampledTime = Math.Min(_elapsed, _duration);
        Sample(_sampledTime);
        if (_death)
        {
            Transform2D current = _marker.GlobalTransform;
            Transform2D pose = current * _reference.AffineInverse() * _baseline;
            _displayCenter = _baseline * _face.Position + current.Origin - _reference.Origin;
            pose.Origin = _displayCenter - pose.X * _face.Position.X - pose.Y * _face.Position.Y;
            _card.GlobalTransform = pose;
        }
        else
        {
            Transform2D current = _driver.GlobalTransform.AffineInverse() * _marker.GlobalTransform;
            Transform2D change = current * _heldReference.AffineInverse();
            change.Origin = Vector2.Zero;
            Transform2D direction = new(new Vector2(_mirrored ? -1f : 1f, 0f), Vector2.Down, Vector2.Zero);
            change = direction * change * direction;
            bool returning = _sampledTime >= ReturnSeconds;
            Transform2D grip = returning
                ? _scene.GetGlobalTransform().AffineInverse() * _follow!()
                : new Transform2D(0f, new Vector2(_heldReference.X.Length(), _heldReference.Y.Length()), 0f, Vector2.Zero);
            Transform2D pose = change * grip;
            Vector2 center = returning
                ? grip * _face.Position + direction * (current.Origin - _heldReference.Origin)
                : _sourceCenter + direction * (current.Origin - _sourceReference);
            // Keep the native target-to-hand flight, including its authored
            // shrink and return. The attacker's return side does not redirect it.
            pose.Origin = center - pose.X * _face.Position.X - pose.Y * _face.Position.Y;
            _card.GlobalTransform = _scene.GetGlobalTransform() * pose;
            _displayCenter = _scene.GetGlobalTransform() * center;
        }
        if (_elapsed < _duration + (_death ? 0.5f : 0f)) return;
        if (_death) _card.QueueFreeSafely();
        else { _card.Visible = true; _completed?.Invoke(); }
        Stop();
    }

    internal void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        RenderingServer.FramePreDraw -= ApplyPose;
        this.QueueFreeSafely();
    }

    public override void _ExitTree()
    {
        if (_stopped) return;
        _stopped = true;
        RenderingServer.FramePreDraw -= ApplyPose;
    }
}
