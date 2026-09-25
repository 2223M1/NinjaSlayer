using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Cards;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Code.Combat;
using V2 = System.Numerics.Vector2;

namespace NinjaSlayer.Code.Nodes;

public partial class NinjaSlayerAimPose
{
    private float? _somersault;
    private Vector2 _somersaultLift;
    private FreeControlMotionBlur? _somersaultBlur;
    private double _somersaultClock;

    internal static bool IsSomersaultHeavy(CardModel? card) =>
        card is CollapseFistRedesignV1 or Slaughter or StraightKiRedesignV1;

    internal void ApplySomersault(float progress)
    {
        _somersault = Mathf.Clamp(progress, 0f, 1f);
        SyncNow();
    }

    internal async Task PlaySomersaultInPlace(float seconds)
    {
        await TweenPose(seconds, ApplySomersault, smooth: false);
    }

    internal void ReturnFromSomersault()
    {
        if (_somersault == null) return;
        _somersault = null;
        _somersaultBlur?.ClearHistory();
        float seconds = ExternalAnimations.SlowAttackAnimation.SomersaultHalfSeconds;
        if (seconds > 0f && _actor is { Entity.IsDead: false })
        {
            // A finisher still owns movement during its return. This contribution
            // belongs to that return, so it must not use the ordinary-action gate.
            var recovery = new VisualMotion(this, MotionKind.Recovery, seconds, FacingSign);
            _presentations.Add(recovery);
            recovery.Offset = _actor!.GetParent<CanvasItem>().GetGlobalTransformWithCanvas()
                .AffineInverse().BasisXform(_somersaultLift);
            recovery.Height = 70f;
        }
        _somersaultLift = Vector2.Zero;
    }

    private void ComposeSomersault(ReadOnlySpan<V2> offsets, float ground,
        ref float rotation, ref Vector2 core)
    {
        if (_somersault is not { } p) return;
        Vector2 unlifted = core;
        float forward = AttackForwardSign ?? FacingSign;
        float turn = Mathf.SmoothStep(0f, 1f, p);
        rotation += forward * Mathf.Tau * turn;
        // The target is re-read every frame; an earlier jump cannot leave a stale height.
        core.Y = Mathf.Lerp(core.Y, TargetCanvas().Y, turn) - 4f * 80f * p * (1f - p);
        core.Y = Math.Min(core.Y, ground - GroundedPoseMath.SupportY(offsets, rotation));
        _somersaultLift = core - unlifted;
    }

    private void RecordSomersaultBlur()
    {
        if (HellTornado?.Active == true)
        {
            _somersaultBlur?.ClearHistory();
            return;
        }
        VisualMotion? planar = _presentations.LastOrDefault(motion => motion.PlanarBlur);
        if ((_somersault == null && _tomoe == null && planar == null) || _actor == null)
        {
            _somersaultBlur?.ClearHistory();
            return;
        }
        if (!CanProcess() || Engine.TimeScale <= 0d) return;
        if (_somersaultBlur == null)
        {
            _somersaultBlur = new FreeControlMotionBlur { Name = "SomersaultExposure", ShowBehindParent = true };
            _actor.Visuals.AddChild(_somersaultBlur);
        }
        Sprite2D source = NinjaSlayerVisualRig.GetBodySprite(_actor.Visuals)!;
        var overlay = GetNode<NarakuVisualOverlay>("NarakuVisualOverlay");
        Sprite2D body = overlay.Visible ? overlay : source;
        if (_tomoe != null || planar != null)
        {
            float angle = _tomoe?.Angle ?? planar!.Radians;
            Transform2D space = new(0f, CoreCanvas);
            Transform2D authored = new Transform2D(-angle, Vector2.Zero)
                * space.AffineInverse() * body.GetGlobalTransformWithCanvas();
            _somersaultBlur.RecordHistory(body, _somersaultClock, space, Vector2.Zero, authored, angle);
        }
        else _somersaultBlur.RecordHistory(body, _somersaultClock, CoreCanvas);
    }
}
