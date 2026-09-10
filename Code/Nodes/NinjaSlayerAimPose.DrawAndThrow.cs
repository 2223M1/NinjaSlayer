using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using V2 = System.Numerics.Vector2;

namespace NinjaSlayer.Code.Nodes;

public partial class NinjaSlayerAimPose
{
    internal static float ShurikenWindupSeconds => CombatActionTimingRuntime.Resolve(0.166f, 0.083f);
    private float _bodyHeight;
    private float _flipDuration;
    private float _flipElapsed;
    private float _flipDirection;
    private float _flipHeight;
    private float _throwDuration;
    private float _throwElapsed;
    private float _throwReturnDuration;
    private float _throwStartAngle;
    private Vector2 _throwStartOffset;
    private Vector2 _throwOffset;
    private float _throwFacing;
    private bool _throwReleased;
    private Creature? _throwTarget;
    private float _baseDisplayAngle;
    private float _baseEffectiveTravelY;
    private readonly List<AirMotion> _airMotions = [];

    internal bool IsBackflipping => _flipDuration > 0f;
    private bool HasPresentation => IsBackflipping || _throwDuration > 0f || _airMotions.Count > 0;

    internal bool IsJumping => _airMotions.Any(motion => !motion.Hop);

    internal void BeginAirMotion(bool hop)
    {
        if (_actor == null || _actor.Entity.IsDead || _exclusive
            || NinjaSlayerFinisherCinematic.IsMovementOwned(_actor.Entity)) return;
        float duration = CombatActionTimingRuntime.Resolve(hop ? 0.28f : 0.7f, hop ? 0.14f : 0.35f);
        if (duration <= 0f) return;
        NinjaSlayerRapidAnimationCoordinator.EnsureLifecycle(_actor.Entity);
        _airMotions.Add(new AirMotion(duration, hop ? 60f : 150f, hop));
    }

    internal void ClearAirMotions() => _airMotions.Clear();

    private sealed class AirMotion(float duration, float height, bool hop)
    {
        internal readonly float Duration = duration;
        internal readonly float Height = height;
        internal readonly bool Hop = hop;
        internal float Elapsed;
    }

    internal void BeginBackflip()
    {
        if (_actor == null || _actor.Entity.IsDead || _exclusive
            || NinjaSlayerFinisherCinematic.IsMovementOwned(_actor.Entity) || IsBackflipping) return;
        SyncNow();
        float duration = CombatActionTimingRuntime.Resolve(2f / 3f, 1f / 3f);
        if (duration <= 0f) return;
        NinjaSlayerRapidAnimationCoordinator.EnsureLifecycle(_actor.Entity);
        _flipDuration = duration;
        _flipElapsed = 0f;
        _flipDirection = -FacingSign;
        _flipHeight = _bodyHeight * 0.22f;
        SyncNow();
    }

    internal void BeginShurikenThrow(Creature? target)
    {
        float seconds = ShurikenWindupSeconds;
        if (_actor == null || _actor.Entity.IsDead || _exclusive || seconds <= 0f
            || NinjaSlayerFinisherCinematic.IsMovementOwned(_actor.Entity))
            return;
        NinjaSlayerRapidAnimationCoordinator.EnsureLifecycle(_actor.Entity);
        (float angle, _) = ThrowPose();
        _throwTarget = target;
        _throwFacing = target?.GetCreatureNode() is { } node
            ? (node.Visuals.VfxSpawnPosition.GetGlobalTransformWithCanvas().Origin.X < CoreCanvas.X ? -1f : 1f)
            : FacingSign;
        _throwStartAngle = angle;
        _throwStartOffset = _throwOffset;
        _throwDuration = seconds;
        _throwReturnDuration = CombatActionTimingRuntime.Resolve(0.168f, 0.084f);
        _throwElapsed = 0f;
        _throwReleased = false;
        SyncNow();
    }

    private (float Angle, float Distance) ThrowPose()
    {
        if (_throwDuration <= 0f) return (0f, 0f);
        float forward = Mathf.DegToRad(8f) * _throwFacing;
        if (_throwReleased)
        {
            float remaining = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp(_throwElapsed / _throwReturnDuration, 0f, 1f));
            return (forward * remaining, 12f * remaining);
        }
        float p = Mathf.Clamp(_throwElapsed / _throwDuration, 0f, 1f);
        float backward = -Mathf.DegToRad(6f) * _throwFacing;
        if (p < 0.25f)
        {
            float blend = Mathf.SmoothStep(0f, 1f, p / 0.25f);
            return (Mathf.Lerp(_throwStartAngle, backward, blend), -4f * blend);
        }
        float send = Mathf.SmoothStep(0f, 1f, Mathf.Clamp((p - 0.25f) / 0.45f, 0f, 1f));
        return (Mathf.Lerp(backward, forward, send), Mathf.Lerp(-4f, 12f, send));
    }

    private void AdvancePresentation(float delta)
    {
        for (int i = _airMotions.Count - 1; i >= 0; i--)
            if ((_airMotions[i].Elapsed += delta) >= _airMotions[i].Duration) _airMotions.RemoveAt(i);
        if (IsBackflipping && (_flipElapsed += delta) >= _flipDuration)
            _flipDuration = 0f;
        if (_throwDuration > 0f)
        {
            _throwElapsed += delta;
            if (!_throwReleased && _throwElapsed >= _throwDuration)
            {
                _throwElapsed -= _throwDuration;
                _throwReleased = true;
            }
            if (_throwReleased && _throwElapsed >= _throwReturnDuration) _throwDuration = 0f;
        }
    }

    private void ClearPresentation()
    {
        _flipDuration = _throwDuration = 0f;
        ClearAirMotions();
        _throwTarget = null;
        _throwOffset = Vector2.Zero;
    }

    private void ComposePresentation(ReadOnlySpan<V2> offsets, ref float rotation, ref Vector2 core)
    {
        _baseDisplayAngle = rotation;
        _baseEffectiveTravelY = _effectiveTravelY;
        Vector2 baseline = core;
        float ground = core.Y + GroundedPoseMath.SupportY(offsets, rotation);
        Transform2D parentCanvas = _actor!.GetParent<CanvasItem>().GetGlobalTransformWithCanvas();
        float angle = 0f;
        Vector2 shift = Vector2.Zero;
        foreach (AirMotion motion in _airMotions)
        {
            float p = motion.Elapsed / motion.Duration;
            shift.Y -= motion.Height * (motion.Hop ? Mathf.Sin(p * Mathf.Pi) : 4f * p * (1f - p));
        }
        if (IsBackflipping)
        {
            float p = _flipElapsed / _flipDuration;
            angle += _flipDirection * Mathf.Tau * p;
            shift.Y -= 4f * _flipHeight * p * (1f - p);
        }
        (float throwAngle, float distance) = ThrowPose();
        angle += throwAngle;
        Vector2 direction = Vector2.Right * _throwFacing;
        if (_throwTarget?.IsAlive == true && _throwTarget.GetCreatureNode() is { } targetNode)
        {
            Vector2 target = targetNode.Visuals.VfxSpawnPosition.GetGlobalTransformWithCanvas().Origin;
            direction = parentCanvas.AffineInverse().BasisXform(target - core).Normalized();
        }
        _throwOffset = direction * distance;
        if (_throwDuration > 0f && !_throwReleased && _throwElapsed < _throwDuration * 0.25f)
            _throwOffset = _throwStartOffset.Lerp(direction * -4f,
                Mathf.SmoothStep(0f, 1f, _throwElapsed / (_throwDuration * 0.25f)));
        shift += parentCanvas.BasisXform(_throwOffset);
        rotation += angle;
        core += shift;
        if (HasPresentation) core.Y = Math.Min(core.Y, ground - GroundedPoseMath.SupportY(offsets, rotation));
        _effectiveTravelY += parentCanvas.AffineInverse().BasisXform(core - baseline).Y;
    }
}
