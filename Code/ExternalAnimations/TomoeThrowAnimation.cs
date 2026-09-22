using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Nodes;

namespace NinjaSlayer.Code.ExternalAnimations;

internal sealed partial class TomoeThrowAnimation : Node
{
    private const float ReleaseSeconds = .155f;
    private const float ImpactSeconds = .40f;
    private const float EndSeconds = .55f;
    private NCreature _actor = null!;
    private NCreature _target = null!;
    private NCombatRoom _room = null!;
    private NinjaSlayerAimPose _pose = null!;
    private NinjaSlayerAimPose.TomoePose _ownerMotion = null!;
    private GrappledTargetPose? _victim;
    private NinjaSlayerFreeControl.CinematicLease? _freeControl;
    private Func<Task> _onImpact = null!;
    private readonly TaskCompletionSource _resolved = new();
    private Vector2 _releaseCore;
    private float _elapsed;
    private bool _released;
    private bool _impacted;
    private bool _stopped;

    internal static Task Play(Creature owner, Creature target, Func<Task> onImpact)
    {
        if (CombatActionTimingRuntime.VisualSeconds(ImpactSeconds) <= 0f
            || NinjaSlayerAimPose.Get(owner) is not { } pose
            || owner.GetCreatureNode() is not { } actor || target.GetCreatureNode() is not { } victim
            || NCombatRoom.Instance is not { } room || NinjaSlayerFinisherCinematic.IsMovementOwned(owner))
            return onImpact();

        GrappledTargetPose.Release(target);
        var motion = new TomoeThrowAnimation
        {
            Name = "TomoeThrow", ProcessPriority = 80, _actor = actor, _target = victim,
            _pose = pose, _room = room, _onImpact = onImpact
        };
        actor.Visuals.AddChild(motion);
        return motion._resolved.Task;
    }

    internal static float Smooth(float p) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp(p, 0f, 1f));

    public override void _Ready()
    {
        try
        {
            _freeControl = NinjaSlayerFreeControl.Get(_actor.Entity)?.SuspendForCinematic(
                NinjaSlayerRapidAnimationCoordinator.GetBaseline(_actor.Entity, _actor));
            _victim = new GrappledTargetPose(_target, blur: true);
            _ownerMotion = _pose.BeginTomoe(_target.Entity);
            _target.TreeExiting += TargetExiting;
            RenderingServer.FramePreDraw += SyncTarget;
            SyncTarget();
        }
        catch (Exception error)
        {
            _resolved.TrySetException(error);
            Stop();
        }
    }

    public override void _Process(double delta)
    {
        if (_stopped) return;
        if (!_actor.Entity.IsAlive || NCombatRoom.Instance != _room
            || NinjaSlayerFinisherCinematic.IsMovementOwned(_actor.Entity))
        {
            Stop();
            return;
        }
        float next = Math.Min(EndSeconds, _elapsed + (float)delta);
        // Sample phase boundaries even when a render frame straddles the release or impact.
        if (!_released && next >= ReleaseSeconds)
        {
            _elapsed = ReleaseSeconds;
            _pose.ApplyTomoe(_ownerMotion, _elapsed);
            SyncTarget();
            if (_victim != null) _releaseCore = _victim.Core;
            _released = true;
        }
        if (!_impacted && next >= ImpactSeconds)
        {
            _elapsed = ImpactSeconds;
            _pose.ApplyTomoe(_ownerMotion, _elapsed);
            SyncTarget();
            _impacted = true;
            _pose.ReleaseTomoe(_ownerMotion, recover: true);
            _freeControl?.Dispose();
            _freeControl = null;
            if (_victim is { IsActive: true } && _target.Entity.IsAlive)
            {
                SfxCmd.Play("event:/sfx/enemy/enemy_attacks/thieving_hopper/thieving_hopper_land");
                NGame.Instance?.ScreenShake(ShakeStrength.Medium, ShakeDuration.Short);
            }
            _ = ResolveImpact();
        }
        _elapsed = next;
        if (!_impacted && !_pose.ApplyTomoe(_ownerMotion, _elapsed)) { Stop(); return; }
        SyncTarget();
        if (_elapsed >= EndSeconds) Stop();
    }

    private async Task ResolveImpact()
    {
        try { await _onImpact(); _resolved.TrySetResult(); }
        catch (Exception error) { _resolved.TrySetException(error); }
    }

    private void SyncTarget()
    {
        if (_stopped || _victim is not { IsActive: true }) return;
        if (!_target.Entity.IsAlive || NinjaSlayerFinisherCinematic.IsMovementOwned(_target.Entity))
        {
            _victim?.Dispose();
            return;
        }
        float time = _elapsed;
        float direction = -_ownerMotion.Facing;
        Vector2 core;
        float angle;
        if (time <= ReleaseSeconds)
        {
            float p = Smooth((time - .05f) / .08f);
            core = _victim.Start.Lerp(_victim.FromCanvas(_ownerMotion.FootCanvas), p);
            angle = direction * Mathf.DegToRad(65f) * p;
        }
        else if (time <= .27f)
        {
            float p = Mathf.Clamp((time - ReleaseSeconds) / (.27f - ReleaseSeconds), 0f, 1f);
            core = _releaseCore.Lerp(_victim.Peak, 1f - (1f - p) * (1f - p));
            angle = direction * Mathf.DegToRad(65f + 260f * Smooth(p));
        }
        else if (time <= ImpactSeconds)
        {
            float p = Mathf.Clamp((time - .27f) / .13f, 0f, 1f);
            angle = direction * Mathf.DegToRad(325f + 125f * p * p);
            Vector2 landing = new(_victim.Start.X, _victim.GroundAt(direction * Mathf.DegToRad(450f)));
            core = _victim.Peak.Lerp(landing, p * p);
        }
        else
        {
            float p = Smooth((time - .425f) / .125f);
            angle = direction * Mathf.DegToRad(450f - 90f * p);
            core = new(_victim.Start.X, Mathf.Lerp(_victim.GroundAt(angle), _victim.Start.Y, p));
        }
        _victim.Apply(core, angle);
    }

    private void TargetExiting() => _victim?.Dispose();

    private void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        RenderingServer.FramePreDraw -= SyncTarget;
        if (GodotObject.IsInstanceValid(_target)) _target.TreeExiting -= TargetExiting;
        _victim?.Dispose();
        _victim = null;
        if (_ownerMotion != null && GodotObject.IsInstanceValid(_pose)) _pose.ReleaseTomoe(_ownerMotion, recover: false);
        _freeControl?.Dispose();
        _freeControl = null;
        if (!_impacted) _resolved.TrySetResult();
        if (IsInsideTree() && !IsQueuedForDeletion()) QueueFree();
    }

    public override void _ExitTree() => Stop();

}
