using Godot;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Nodes;

namespace NinjaSlayer.Code.ExternalAnimations;

internal sealed partial class HookRopeAnimation : Node
{
    private const float TightenSeconds = CombatActionTiming.CastNormalSeconds;
    private const float EndSeconds = .5f;
    private NCreature _actor = null!;
    private NCreature _target = null!;
    private NCombatRoom _room = null!;
    private NinjaSlayerAimPose _pose = null!;
    private NinjaSlayerAimPose.VisualMotion? _ownerMotion;
    private GrappledTargetPose? _victim;
    private float _sourceTime;
    private float _rate;
    private float _facing;
    private bool _stopped;

    internal static async Task Play(Creature owner, Creature target, Func<Task> onTighten)
    {
        float castSeconds = CombatActionTimingRuntime.CastSeconds;
        NCombatRoom? room = NCombatRoom.Instance;
        HookRopeAnimation? motion = null;
        try
        {
            if (castSeconds > 0f && room != null && NinjaSlayerAimPose.Get(owner) is { } pose
                && owner.GetCreatureNode() is { } actor && target.GetCreatureNode() is { } victim
                && !NinjaSlayerFinisherCinematic.IsMovementOwned(owner))
            {
                motion = new HookRopeAnimation
                {
                    Name = "HookRope", ProcessPriority = 80, _actor = actor, _target = victim,
                    _room = room, _pose = pose, _rate = TightenSeconds / castSeconds
                };
                actor.Visuals.AddChild(motion);
                motion.Start();
            }

            // Cast settlement is independent of visual interruption and recovery.
            if (castSeconds > 0f) await Cmd.Wait(castSeconds);
            if (!owner.IsAlive || !target.IsAlive || NCombatRoom.Instance != room) return;
            if (GodotObject.IsInstanceValid(motion) && !motion._stopped)
            {
                motion._sourceTime = Math.Max(TightenSeconds, motion._sourceTime);
                motion.SyncPose();
            }
            await onTighten();
        }
        catch
        {
            if (GodotObject.IsInstanceValid(motion)) motion.Stop();
            throw;
        }
    }

    private void Start()
    {
        _victim = new GrappledTargetPose(_target, blur: false);
        _facing = _target.GlobalPosition.X < _actor.GlobalPosition.X ? -1f : 1f;
        _ownerMotion = _pose.BeginVisualMotion(NinjaSlayerAimPose.MotionKind.Offset, EndSeconds / _rate);
        if (_ownerMotion != null) _ownerMotion.Paused = true;
        NDebugAudioManager.Instance?.Play(TmpSfx.daggerThrow);
        _target.TreeExiting += TargetExiting;
        RenderingServer.FramePreDraw += SyncPose;
        SyncPose();
    }

    public override void _Process(double delta)
    {
        if (_stopped) return;
        if (!_actor.Entity.IsAlive || !_target.Entity.IsAlive || _victim is not { IsActive: true }
            || NCombatRoom.Instance != _room || NinjaSlayerFinisherCinematic.IsMovementOwned(_actor.Entity)
            || NinjaSlayerFinisherCinematic.IsMovementOwned(_target.Entity))
        {
            Stop();
            return;
        }
        _sourceTime = Math.Min(EndSeconds, _sourceTime + (float)delta * _rate);
        SyncPose();
        if (_sourceTime >= EndSeconds) Stop();
    }

    private void SyncPose()
    {
        if (_stopped || _victim is not { IsActive: true }) return;
        float recover = 1f - Smooth((_sourceTime - (EndSeconds - .15f)) / .15f);
        float pull = Smooth((_sourceTime - (TightenSeconds - .05f)) / .05f) * recover;
        _victim.Trip(pull, _facing);
        if (_ownerMotion is { Active: true })
        {
            float windup = Mathf.Sin(Mathf.Pi * Mathf.Clamp(_sourceTime / (TightenSeconds * .6f), 0f, 1f));
            _ownerMotion.Offset = Vector2.Left * _facing * (6f * windup + 10f * pull);
            _ownerMotion.Radians = -_facing * Mathf.DegToRad(5f * windup + 8f * pull);
            _pose.SyncNow();
        }
    }

    private void TargetExiting() => Stop();
    private static float Smooth(float p) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp(p, 0f, 1f));

    private void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        RenderingServer.FramePreDraw -= SyncPose;
        if (GodotObject.IsInstanceValid(_target)) _target.TreeExiting -= TargetExiting;
        _victim?.Dispose();
        _ownerMotion?.Dispose();
        if (IsInsideTree() && !IsQueuedForDeletion()) QueueFree();
    }

    public override void _ExitTree() => Stop();
}
