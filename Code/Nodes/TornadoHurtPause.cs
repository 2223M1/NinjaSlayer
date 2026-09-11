using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Code.ExternalAnimations;

namespace NinjaSlayer.Code.Nodes;

internal sealed partial class TornadoHurtPause : Node
{
    private NCreature _target = null!;
    private Action? _resumeStagger;
    private MegaTrackEntry? _track;
    private float _trackTime;
    private float _trackSpeed;
    private float _remaining;

    internal static void Cancel(Creature target) => target.GetCreatureNode()
        ?.GetNodeOrNull<TornadoHurtPause>("TornadoHurtPause")?.Finish();

    internal static void Start(Creature target, float seconds)
    {
        if (target.GetCreatureNode() is not { } node) return;
        Cancel(target);
        var pause = new TornadoHurtPause { Name = "TornadoHurtPause", _target = node, _remaining = seconds };
        pause._resumeStagger = StaggerAnimation.PauseCurrent(target);
        if (pause._resumeStagger == null && node.SpineAnimation.IsValid)
        {
            MegaTrackEntry? track = node.SpineAnimation.GetCurrentTrack();
            if (track?.GetAnimationName() == "hurt")
            {
                pause._track = track;
                pause._trackTime = track.GetTrackTime();
                pause._trackSpeed = track.BoundObject.Call("get_time_scale").AsSingle();
                track.SetTimeScale(0f);
            }
            else (track as IDisposable)?.Dispose();
        }
        if (pause._resumeStagger == null && pause._track == null) { pause.Free(); return; }
        node.AddChild(pause);
    }

    public override void _Process(double delta)
    {
        _remaining -= (float)delta;
        if (_remaining <= 0f || _target.Entity.IsDead || FinisherSessionRegistry.GetActiveSession() != null) Finish();
    }

    private void Finish()
    {
        Resume();
        if (GetParent() is { } parent) parent.RemoveChild(this);
        QueueFree();
    }

    private void Resume()
    {
        _resumeStagger?.Invoke();
        _resumeStagger = null;
        if (_track == null) return;
        try
        {
            if (GodotObject.IsInstanceValid(_target) && !_target.Entity.IsDead && _target.SpineAnimation.IsValid)
            {
                MegaTrackEntry? current = _target.SpineAnimation.GetCurrentTrack();
                using IDisposable? lease = current as IDisposable;
                if (current?.GetAnimationName() == "hurt" && Mathf.IsEqualApprox(current.GetTrackTime(), _trackTime))
                    _track.SetTimeScale(_trackSpeed);
            }
        }
        finally { (_track as IDisposable)?.Dispose(); _track = null; }
    }

    public override void _ExitTree() => Resume();
}
