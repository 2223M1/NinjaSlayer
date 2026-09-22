using System.Numerics;

namespace NinjaSlayer.Code.Combat;

internal static class DragPoseMath
{
    internal const float TurnSeconds = .15f;

    internal static float Smooth(float value)
    {
        float p = Math.Clamp(value, 0f, 1f);
        return p * p * (3f - 2f * p);
    }

    internal static float TurnAngle(float from, float to, float elapsed, float duration) =>
        duration <= 0f ? to : from + (to - from) * Smooth(elapsed / duration);

    internal static DragAimSample AimSample(ReadOnlySpan<Vector2> contour, Vector2 core, Vector2 pointer,
        ReadOnlySpan<Vector2> targets, float line, float reference, float previous)
    {
        float minimum = 0f, maximum = 0f;
        foreach (Vector2 target in targets)
        {
            float angle = GroundedPoseMath.AimAngle(contour, core, target, line, reference, 0f);
            minimum = Math.Min(minimum, angle);
            maximum = Math.Max(maximum, angle);
        }
        if (minimum == maximum) return new(minimum, minimum, maximum);
        return new(GroundedPoseMath.AimAngle(contour, core, pointer, line, reference, previous), minimum, maximum);
    }
}

internal readonly record struct DragAimSample(float Raw, float Minimum, float Maximum)
{
    internal float Limited => Math.Clamp(Raw, Minimum, Maximum);
}

internal struct DragAimFollow
{
    internal const float MaximumOvershoot = 3f * MathF.PI / 180f;
    private const float InteriorMargin = MathF.PI / 180f;
    private const float PushThreshold = 5f * MathF.PI / 180f;
    private float _angle, _velocity, _rebound, _reboundVelocity;
    private float _idleSeconds, _pendingPeak;
    private int _pushedSide, _pendingSide;

    internal readonly float Angle => _angle + _rebound;
    internal readonly float Velocity => _velocity + _reboundVelocity;

    internal void Reset(float angle)
    {
        this = default;
        _angle = angle;
    }

    internal float Advance(DragAimSample aim, float pointerVelocity, bool canPush, float delta)
    {
        if (delta <= 0f) return Angle;
        float before = Angle;
        int side = aim.Raw < aim.Minimum ? -1 : aim.Raw > aim.Maximum ? 1 : 0;
        bool outward = canPush && side != 0 && pointerVelocity * side > PushThreshold;
        if (!canPush || aim.Minimum == aim.Maximum)
        {
            _pushedSide = _pendingSide = 0;
            _pendingPeak = _idleSeconds = 0f;
        }
        else
        {
            if (aim.Raw >= aim.Minimum + InteriorMargin && aim.Raw <= aim.Maximum - InteriorMargin)
                _pushedSide = 0;
            if (outward)
            {
                if (_pushedSide != side || _idleSeconds >= .12f)
                {
                    _pendingSide = _pushedSide = side;
                    _pendingPeak = Math.Min(MaximumOvershoot, Math.Abs(pointerVelocity) * .01f);
                }
                _idleSeconds = 0f;
            }
            else _idleSeconds += delta;
            if (side != _pendingSide) { _pendingSide = 0; _pendingPeak = 0f; }
        }

        Damp(ref _angle, ref _velocity, aim.Limited, 28f, delta);
        if (_pendingSide != 0 && Math.Abs(_angle - aim.Limited) <= InteriorMargin)
        {
            // A velocity impulse peaks at v / (e * frequency). Wait until the
            // body reaches the edge so a quick pointer flick is not lost in pursuit.
            _reboundVelocity = _pendingSide * _pendingPeak * MathF.E * 40f;
            _pendingSide = 0;
            _pendingPeak = 0f;
        }
        Damp(ref _rebound, ref _reboundVelocity, 0f, 40f, delta);

        // A shrinking legal sector must converge from the current pose, not snap.
        float lower = Math.Min(aim.Minimum - MaximumOvershoot, before);
        float upper = Math.Max(aim.Maximum + MaximumOvershoot, before);
        float bounded = Math.Clamp(Angle, lower, upper);
        if (bounded != Angle)
        {
            _angle = bounded - _rebound;
            _velocity = _reboundVelocity = 0f;
        }
        return Angle;
    }

    private static void Damp(ref float value, ref float velocity, float target, float frequency, float delta)
    {
        float offset = value - target;
        float coefficient = velocity + frequency * offset;
        float decay = MathF.Exp(-frequency * delta);
        value = target + (offset + coefficient * delta) * decay;
        velocity = (velocity - frequency * coefficient * delta) * decay;
        if (Math.Abs(value - target) < .00001f && Math.Abs(velocity) < .0001f)
        {
            value = target;
            velocity = 0f;
        }
    }
}

internal readonly record struct TornadoChargeProfile(
    float Back, float Seconds, float ScaleX, float ScaleY, float TrembleX, float TrembleY, float Hertz)
{
    internal static TornadoChargeProfile Default => new(32f, .25f, 1.04f, .94f, 1.3f, .008f, 10f);

    internal (float Back, Vector2 Scale) Sample(float elapsed)
    {
        float p = DragPoseMath.Smooth(elapsed / Seconds);
        float tremble = DragPoseMath.Smooth((elapsed / Seconds - .5f) * 2f);
        float wave = MathF.Sin(MathF.Tau * Hertz * elapsed);
        return (Back * p + TrembleX * tremble * wave,
            new(1f + (ScaleX - 1f) * p, 1f + (ScaleY - 1f) * p + TrembleY * tremble * wave));
    }
}
