using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.Code.Nodes;

internal sealed partial class NativeStolenCardMotion : Node
{
    internal const string ScenePath = "res://scenes/creature_visuals/thieving_hopper.tscn";
    private Node2D _card = null!;
    private NCreatureVisuals _driver = null!;
    private Node2D _spine = null!;
    private Marker2D _marker = null!;
    private MegaTrackEntry _track = null!;
    private Transform2D _reference;
    private Transform2D _baseline;
    private float _duration;
    private float _elapsed;
    private bool _death;
    private bool _stopped;
    private bool _playedSound;
    private float _targetOffsetX;
    private Action? _completed;
    private Func<Transform2D>? _follow;

    internal static NativeStolenCardMotion Create(Node2D card, bool death, Action? completed = null,
        Func<Transform2D>? follow = null, float targetOffsetX = 0f)
    {
        var motion = new NativeStolenCardMotion
        {
            _card = card, _death = death, _completed = completed, _baseline = card.GlobalTransform,
            _follow = follow, _targetOffsetX = targetOffsetX
        };
        NCombatRoom.Instance!.SceneContainer.AddChildSafely(motion);
        return motion;
    }

    public override void _Ready()
    {
        _driver = PreloadManager.Cache.GetScene(ScenePath).Instantiate<NCreatureVisuals>();
        _driver.Visible = false;
        _driver.ProcessMode = ProcessModeEnum.Disabled;
        AddChild(_driver);
        _spine = _driver.GetCurrentBody();
        _marker = _driver.GetNode<Marker2D>("%StolenCardPos");
        if (!_death) _driver.GetNode<Node2D>("Visuals/SpineBoneNode").Position = Vector2.Right * _targetOffsetX;
        var animationState = _driver.SpineBody!.GetAnimationState();
        animationState.SetAnimation(_death ? "die" : "steal", false);
        _track = animationState.GetCurrent(0)!;
        _track.SetMixDuration(0f);
        _duration = _track.GetAnimationDuration();
        Sample(_death ? 0f : _duration);
        _reference = _marker.GlobalTransform;
        Sample(0f);
        if (!_death) _card.Visible = false;
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
        if (!_death && !_playedSound && _elapsed >= CombatActionTimingRuntime.TriggerSeconds(.25f))
        {
            _playedSound = true;
            SfxCmd.Play("event:/sfx/enemy/enemy_attacks/thieving_hopper/thieving_hopper_steal");
        }
        Sample(Math.Min(_elapsed, _duration));
        Transform2D current = _marker.GlobalTransform;
        if (_follow != null) _baseline = _follow();
        Transform2D change = current * _reference.AffineInverse();
        Transform2D pose = change * _baseline;
        // Keep the native world-space displacement while rebasing to the actual grip.
        pose.Origin = _baseline.Origin + current.Origin - _reference.Origin;
        _card.GlobalTransform = pose;
        if (!_death)
            _card.Visible = _elapsed >= CombatActionTimingRuntime.TriggerSeconds(0.25f)
                + CombatActionTimingRuntime.TriggerSeconds(0.6f);
        if (_elapsed < _duration + (_death ? 0.5f : 0f)) return;
        if (_death) _card.QueueFreeSafely();
        else { _card.Visible = true; _completed?.Invoke(); }
        Stop();
    }

    internal void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        this.QueueFreeSafely();
    }
}
