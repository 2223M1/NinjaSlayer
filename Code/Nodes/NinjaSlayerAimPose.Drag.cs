using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using V2 = System.Numerics.Vector2;

namespace NinjaSlayer.Code.Nodes;

public partial class NinjaSlayerAimPose
{
    private CardModel? _dragCard;
    private readonly List<V2> _aimTargets = [];
    private float _turnAngle;
    private float _turnFrom;
    private float _turnTo;
    private float _turnElapsed;
    private float _turnDuration;
    private Func<double, double>? _turnExposure;
    private VerticalAxisSpinProjection? _turnProjection;
    private VerticalAxisSpinProjection? _externalSpin;
    private float _externalSpinDegrees;
    private Func<double, double>? _externalSpinExposure;
    private bool _turnReplaying;
    private bool _chargePending;
    private CardModel? _chargeCard;
    private Vector2 _chargeScale = Vector2.One;
    private Vector2 _chargeActionScale = Vector2.One;
    private Vector2 _returnChargeScale = Vector2.One;
    private Vector2 _chargeStartScale = Vector2.One;
    private float _chargeStartBack;

    internal TornadoChargeProfile ChargeProfile { get; set; } = TornadoChargeProfile.Default;
    private bool Turning => _turnDuration > 0f;
    private bool HasCharge => _charging || _chargePending || _chargeScale != Vector2.One || _chargeBack != 0f;
    private bool ChargePreview => !IsBusy && (_charging || _chargePending || _dragCard is TornadoFistRedesignV1);

    private void RequestTurn(bool left)
    {
        if (_actor == null) return;
        float destination = left ? 180f : 0f;
        if (Turning && _turnTo == destination || !Turning && left == (FacingSign < 0f)) return;
        if (!Turning)
        {
            _turnAngle = FacingSign < 0f ? 180f : 0f;
            if (!SoarSpinAnimation.IsVerticalSpinActive(_actor.Entity) && _spin == null)
            {
                Sprite2D body = NinjaSlayerVisualRig.GetBodySprite(_actor.Visuals)!;
                Transform2D posed = Transform;
                Transform = Transform2D.Identity;
                Node2D focus = NinjaSlayerVisualRig.GetCinematicFocus(_actor.Visuals)!;
                _turnProjection = VerticalAxisSpinProjection.CaptureCurrent(body,
                    focus.GetGlobalTransformWithCanvas().Origin.X, focus.GetGlobalTransformWithCanvas().Origin);
                Transform = posed;
            }
        }
        _turnFrom = _turnAngle;
        _turnTo = destination;
        _turnElapsed = 0f;
        _turnDuration = DragPoseMath.TurnSeconds * Math.Abs(_turnTo - _turnFrom) / 180f;
        if (Mathf.IsZeroApprox(_turnDuration)) FinishTurn(left);
    }

    private void AdvanceTurn(float delta)
    {
        if (!Turning || _actor == null) return;
        _turnElapsed = Math.Min(_turnDuration, _turnElapsed + delta);
        float from = _turnFrom, to = _turnTo, duration = _turnDuration, elapsed = _turnElapsed;
        _turnAngle = DragPoseMath.TurnAngle(from, to, elapsed, duration);
        _turnExposure = age => DragPoseMath.TurnAngle(from, to, Math.Max(0f, elapsed - (float)age), duration);
        bool left = _turnAngle > 90f || Mathf.IsEqualApprox(_turnAngle, 90f) && _turnTo > _turnFrom;
        if (left != (FacingSign < 0f)) NinjaSlayerFacingState.SetFacing(_actor, left);
        ApplyTurnProjection();
        if (_turnElapsed >= _turnDuration) FinishTurn(left);
    }

    private void ApplyTurnProjection()
    {
        if (!Turning || _spin != null) return;
        if (_turnProjection != null) _turnProjection.ApplyDegrees(0f);
        else if (_externalSpin != null)
        {
            _turnReplaying = true;
            try { _externalSpin.ApplyDegrees(_externalSpinDegrees, _externalSpinExposure); }
            finally { _turnReplaying = false; }
        }
    }

    internal (float Degrees, float Basis, Func<double, double>? Before) ComposeFacingSpin(
        VerticalAxisSpinProjection projection, float degrees, Func<double, double>? before)
    {
        if (!ReferenceEquals(projection, _turnProjection) && !ReferenceEquals(projection, _spin) && !_turnReplaying)
        {
            _externalSpin = projection;
            _externalSpinDegrees = degrees;
            _externalSpinExposure = before;
        }
        if (!Turning)
        {
            float settled = _turnAngle;
            return (degrees, settled, before == null ? null : age => before(age) + settled);
        }
        float basis = FacingSign < 0f ? 180f : 0f;
        float yaw = _turnAngle;
        Func<double, double>? turnBefore = _turnExposure;
        return (degrees + yaw - basis, basis,
            age => (before?.Invoke(age) ?? degrees) + (turnBefore?.Invoke(age) ?? yaw));
    }

    private void FinishTurn(bool left)
    {
        if (_actor != null) NinjaSlayerFacingState.SetFacing(_actor, left);
        _turnDuration = 0f;
        _turnAngle = left ? 180f : 0f;
        _turnExposure = null;
        _turnProjection?.Restore();
        _turnProjection = null;
        if (_externalSpin != null && _actor != null && SoarSpinAnimation.IsVerticalSpinActive(_actor.Entity))
            _externalSpin.ApplyDegrees(_externalSpinDegrees, _externalSpinExposure);
        _externalSpin = null;
    }

    private void FaceForAction(bool left, bool immediate)
    {
        if (_actor == null) return;
        if (Turning && !immediate) RequestTurn(left);
        else FinishTurn(left);
    }

    private float PreviewAngle(ReadOnlySpan<V2> offsets, Vector2 core, Vector2 target, float line, float reference)
    {
        if (_dragCard == null || _actor?.Entity.CombatState == null) return 0f;
        if (ReferenceEquals(_hovered, _actor.Entity)) return 0f;
        float axis = _airborne.GetGlobalTransformWithCanvas().Origin.X;
        _aimTargets.Clear();
        foreach (Creature candidate in _actor.Entity.CombatState.Creatures)
        {
            if (ReferenceEquals(candidate, _actor.Entity) || !candidate.IsAlive || !candidate.IsHittable
                || !_dragCard.CanPlayTargeting(candidate) || candidate.GetCreatureNode() is not { } node) continue;
            Vector2 point = node.Visuals.VfxSpawnPosition.GetGlobalTransformWithCanvas().Origin;
            if ((point.X - axis) * FacingSign <= 0f) continue;
            _aimTargets.Add(new(point.X, point.Y));
        }
        // While turning, a pointer on the destination side must not pitch the
        // still-facing-old-side body through the back of its allowed sector.
        if ((target.X - axis) * FacingSign < 0f) return 0f;
        return DragPoseMath.LimitedAngle(offsets, new(core.X, core.Y), new(target.X, target.Y),
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_aimTargets), line, reference, _angle);
    }

    private void UpdateTornadoCharge()
    {
        if (IsBusy || _tornado || _dragOwner == null || _dragCard is not TornadoFistRedesignV1 card) return;
        if (card.ShouldCharge)
        {
            if (_charging) return;
            StopPoseTween();
            _returning = false;
            FinishTurn(FacingSign < 0f);
            _enabled = _charging = true;
            _chargeCard = card;
            _chargePending = false;
            _chargeSeconds = 0f;
            _chargeStartScale = _chargeScale;
            _chargeStartBack = _chargeBack;
            _angle = _kick = 0f;
        }
        else if (_charging)
        {
            BeginReturn();
            _ = ReturnPreview();
        }
        else if (!HasCharge && !_returning)
            _enabled = false;
    }

    private void AdvanceCharge(float delta)
    {
        _chargeSeconds += delta;
        var pose = ChargeProfile.Sample(_chargeSeconds);
        float remainder = 1f - DragPoseMath.Smooth(_chargeSeconds / ChargeProfile.Seconds);
        _chargeBack = -FacingSign * pose.Back + _chargeStartBack * remainder;
        _chargeScale = new Vector2(pose.Scale.X, pose.Scale.Y) + (_chargeStartScale - Vector2.One) * remainder;
    }

    internal void CancelPendingCharge(CardModel? card)
    {
        if (_chargePending && ReferenceEquals(card, _chargeCard)) ReleaseEmptyTornado();
    }

    private void ClearDragPresentation()
    {
        FinishTurn(FacingSign < 0f);
        _dragCard = null;
        _chargeCard = null;
        _chargePending = false;
        _chargeScale = _chargeActionScale = _returnChargeScale = Vector2.One;
    }
}
