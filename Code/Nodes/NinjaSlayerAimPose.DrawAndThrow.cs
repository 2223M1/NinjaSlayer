using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using V2 = System.Numerics.Vector2;

namespace NinjaSlayer.Code.Nodes;

public partial class NinjaSlayerAimPose
{
    internal static float ShurikenWindupSeconds => CombatActionTimingRuntime.VisualSeconds(0.083f);
    private float _bodyHeight;
    private float _baseDisplayAngle;
    private float _baseEffectiveTravelY;
    private Vector2 _presentationScale = Vector2.One;
    private readonly List<VisualMotion> _presentations = [];

    internal bool IsBackflipping => _presentations.Any(m => m.Kind == MotionKind.Backflip);
    internal bool IsJumping => _presentations.Any(m => m.Kind == MotionKind.Jump);
    private bool HasPresentation => _presentations.Count > 0;

    internal enum MotionKind { Hop, Jump, Backflip, Throw, Hurt, Brace, Cast, Debuff, Recovery, Offset }

    // Each lease removes only its own contribution; cleanup never restores a
    // snapshot over another action's live pose.
    internal sealed class VisualMotion(NinjaSlayerAimPose owner, MotionKind kind, float duration, float facing) : IDisposable
    {
        internal readonly MotionKind Kind = kind;
        internal readonly float Duration = duration;
        internal readonly float Facing = facing;
        internal float Elapsed;
        internal float Height;
        internal Creature? Target;
        internal Vector2 Offset;
        internal float Radians;
        internal Vector2 Scale = Vector2.One;
        internal bool Unscaled;
        internal float Rotation;
        internal Vector2 Stretch = Vector2.One;
        internal bool Paused;
        internal bool Active = true;
        private readonly TaskCompletionSource _completion = new();
        internal Task Completion => _completion.Task;

        public void Dispose()
        {
            if (!Active) return;
            Active = false;
            owner._presentations.Remove(this);
            _completion.TrySetResult();
        }
    }

    private bool CanPresent => _actor != null && !_actor.Entity.IsDead && !_exclusive
        && !NinjaSlayerFinisherCinematic.IsMovementOwned(_actor.Entity);

    internal VisualMotion? BeginVisualMotion(MotionKind kind, float duration)
    {
        if (!CanPresent || duration <= 0f) return null;
        NinjaSlayerRapidAnimationCoordinator.EnsureLifecycle(_actor!.Entity);
        var motion = new VisualMotion(this, kind, duration, FacingSign);
        _presentations.Add(motion);
        return motion;
    }

    internal void BeginAirMotion(bool hop)
    {
        float duration = CombatActionTimingRuntime.VisualSeconds(hop ? 0.14f : 0.35f);
        if (BeginVisualMotion(hop ? MotionKind.Hop : MotionKind.Jump, duration) is { } motion)
        {
            motion.Height = hop ? 60f : 150f;
            if (!hop) motion.Elapsed = -KickPreparationSeconds(NinjaSlayerAttackExecution.CurrentPlay);
        }
    }

    internal void ClearAirMotions()
    {
        foreach (VisualMotion motion in _presentations.Where(m => m.Kind is MotionKind.Hop or MotionKind.Jump).ToArray())
            motion.Dispose();
    }

    internal void BeginBackflip()
    {
        if (!CanPresent) return;
        SyncNow();
        if (BeginVisualMotion(MotionKind.Backflip, CombatActionTimingRuntime.VisualSeconds(0.25f)) is { } motion)
            motion.Height = _bodyHeight * 0.30f;
    }

    internal void BeginShurikenThrow(Creature? target)
    {
        if (BeginVisualMotion(MotionKind.Throw, CombatActionTimingRuntime.VisualSeconds(0.167f)) is { } motion)
            motion.Target = target;
    }

    internal Action? PauseHurt()
    {
        VisualMotion? motion = _presentations.LastOrDefault(m => m.Kind == MotionKind.Hurt);
        if (motion == null) return null;
        motion.Paused = true;
        return () => { if (motion.Active) motion.Paused = false; };
    }

    internal bool HasHurt => _presentations.Any(m => m.Kind == MotionKind.Hurt);
    internal void BeginDebuffShake()
    {
        if (HasHurt || _presentations.Any(m => m.Kind == MotionKind.Debuff)) return;
        // NCreature.AnimShake uses a real-time one-second Cubic Out tween even
        // in Fast/Instant. Only its write destination changes for composition.
        BeginVisualMotion(MotionKind.Debuff, 1f);
    }
    internal void ClearHurt()
    {
        foreach (VisualMotion motion in _presentations.Where(m => m.Kind == MotionKind.Hurt).ToArray()) motion.Dispose();
    }

    private void AdvancePresentation(float delta)
    {
        // A completion can synchronously begin another action.
        foreach (VisualMotion motion in _presentations.ToArray())
            if (!motion.Paused && (motion.Elapsed += motion.Unscaled && Engine.TimeScale > 0d ? delta / (float)Engine.TimeScale : delta) >= motion.Duration) motion.Dispose();
    }

    private void ClearPresentation()
    {
        foreach (VisualMotion motion in _presentations.ToArray()) motion.Dispose();
        _presentationScale = Vector2.One;
    }

    private void ComposePresentation(ReadOnlySpan<V2> offsets, ref float rotation, ref Vector2 core)
    {
        _baseDisplayAngle = rotation;
        _baseEffectiveTravelY = _effectiveTravelY;
        _presentationScale = Vector2.One;
        Vector2 baseline = core;
        float ground = core.Y + GroundedPoseMath.SupportY(offsets, rotation);
        Transform2D parentCanvas = _actor!.GetParent<CanvasItem>().GetGlobalTransformWithCanvas();
        ground += parentCanvas.BasisXform(new Vector2(0f, FreeControl?.Altitude ?? 0f)).Y;
        float angle = 0f;
        Vector2 shift = Vector2.Zero;
        foreach (VisualMotion motion in _presentations)
        {
            if (motion.Elapsed < 0f) continue;
            float p = Mathf.Clamp(motion.Elapsed / motion.Duration, 0f, 1f);
            float envelope;
            switch (motion.Kind)
            {
                case MotionKind.Hop:
                case MotionKind.Jump:
                    shift.Y -= motion.Height * (motion.Kind == MotionKind.Hop ? Mathf.Sin(p * Mathf.Pi) : 4f * p * (1f - p));
                    break;
                case MotionKind.Backflip:
                    float turn = Mathf.Clamp(p / 0.88f, 0f, 1f);
                    angle -= motion.Facing * Mathf.Tau * (1f - Mathf.Pow(1f - turn, 1.3f));
                    shift.Y -= 4f * motion.Height * turn * (1f - turn);
                    break;
                case MotionKind.Throw:
                    float windup = motion.Duration * (0.166f / 0.334f);
                    float send = motion.Elapsed / windup;
                    float degrees, distance;
                    if (send < 0.25f)
                    {
                        envelope = Mathf.SmoothStep(0f, 1f, send / 0.25f);
                        degrees = -6f * envelope;
                        distance = -4f * envelope;
                    }
                    else if (send < 1f)
                    {
                        envelope = Mathf.SmoothStep(0f, 1f, Mathf.Clamp((send - 0.25f) / 0.45f, 0f, 1f));
                        degrees = Mathf.Lerp(-6f, 8f, envelope);
                        distance = Mathf.Lerp(-4f, 12f, envelope);
                    }
                    else
                    {
                        envelope = 1f - Mathf.SmoothStep(0f, 1f, (motion.Elapsed - windup) / (motion.Duration - windup));
                        degrees = 8f * envelope;
                        distance = 12f * envelope;
                    }
                    Vector2 direction = Vector2.Right * motion.Facing;
                    if (motion.Target?.IsAlive == true && motion.Target.GetCreatureNode() is { } targetNode)
                        direction = parentCanvas.AffineInverse().BasisXform(
                            targetNode.Visuals.VfxSpawnPosition.GetGlobalTransformWithCanvas().Origin - core).Normalized();
                    angle += Mathf.DegToRad(degrees) * motion.Facing;
                    shift += parentCanvas.BasisXform(direction * distance);
                    break;
                case MotionKind.Hurt:
                    envelope = 1f - p * p;
                    angle -= Mathf.DegToRad(18f) * motion.Facing * envelope;
                    shift += parentCanvas.BasisXform(Vector2.Left * motion.Facing * 28f * envelope);
                    _presentationScale *= new Vector2(1f + 0.04f * envelope, 1f - 0.045f * envelope);
                    break;
                case MotionKind.Debuff:
                    float phase = Mathf.Tau * (1f - Mathf.Pow(1f - p, 3f));
                    float shake = 10f * Mathf.Sin(phase * 4f) * Mathf.Sin(phase * 0.5f);
                    shift += _actor.GetGlobalTransformWithCanvas().BasisXform(Vector2.Right * shake);
                    break;
                case MotionKind.Brace:
                case MotionKind.Cast:
                    envelope = Mathf.Sin(Mathf.Pi * p);
                    angle -= Mathf.DegToRad(motion.Kind == MotionKind.Brace ? 4f : 5f) * motion.Facing * envelope;
                    _presentationScale *= new Vector2(1f + 0.025f * envelope, 1f - 0.04f * envelope);
                    break;
                case MotionKind.Offset:
                    shift += parentCanvas.BasisXform(motion.Offset);
                    angle += motion.Radians;
                    _presentationScale *= motion.Scale;
                    break;
                case MotionKind.Recovery:
                    envelope = 1f - Mathf.SmoothStep(0f, 1f, p);
                    shift += parentCanvas.BasisXform(motion.Offset * envelope);
                    shift.Y -= motion.Height * Mathf.Sin(p * Mathf.Pi);
                    angle += motion.Rotation * envelope;
                    _presentationScale *= Vector2.One.Lerp(motion.Stretch, envelope);
                    break;
            }
        }
        rotation += angle;
        core += shift;
        if (HasPresentation)
        {
            Span<V2> supported = stackalloc V2[offsets.Length];
            for (int i = 0; i < offsets.Length; i++) supported[i] = offsets[i] * new V2(_presentationScale.X, _presentationScale.Y);
            float support = GroundedPoseMath.SupportY(supported, rotation);
            core.Y = Math.Min(core.Y + GroundedPoseMath.SupportY(offsets, rotation) - support, ground - support);
        }
        _effectiveTravelY += parentCanvas.AffineInverse().BasisXform(core - baseline).Y;
    }
}
