using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using V2 = System.Numerics.Vector2;

namespace NinjaSlayer.Code.Nodes;

public partial class NinjaSlayerAimPose
{
    private TomoePose? _tomoe;

    internal sealed class TomoePose
    {
        internal Vector2 Start;
        internal Vector2 Target;
        internal float StartAngle;
        internal float Facing;
        internal float Seconds;
        internal float Angle;
        internal Vector2 FootCanvas;
    }

    internal TomoePose BeginTomoe(Creature target)
    {
        SyncNow();
        Vector2 start = CoreCanvas;
        float startAngle = _displayAngle;
        NinjaSlayerRapidAnimationCoordinator.ClaimExclusiveBaseline(_actor!.Entity, _actor);
        ReturnFromSomersault();
        BeginAction(target, exclusive: true);
        SoarSpinAnimation.SuspendForCinematic(_actor.Entity);
        _chargeScale = Vector2.One;
        Transform2D toLocal = _actor.GetParent<CanvasItem>().GetGlobalTransformWithCanvas().AffineInverse();
        _tomoe = new TomoePose
        {
            Start = toLocal * start,
            Target = toLocal * target.GetCreatureNode()!.Visuals.VfxSpawnPosition.GetGlobalTransformWithCanvas().Origin,
            StartAngle = startAngle,
            Facing = FacingSign
        };
        SyncNow();
        return _tomoe;
    }

    internal bool ApplyTomoe(TomoePose motion, float seconds)
    {
        if (!ReferenceEquals(_tomoe, motion)) return false;
        motion.Seconds = seconds;
        SyncNow();
        return true;
    }

    internal void ReleaseTomoe(TomoePose motion, bool recover)
    {
        if (!ReferenceEquals(_tomoe, motion)) return;
        Vector2 from = CoreCanvas;
        _tomoe = null;
        _travel = Vector2.Zero;
        BeginReturn();
        ApplyReturn(1f);
        if (recover && _actor is { Entity.IsAlive: true })
        {
            var tail = new VisualMotion(this, MotionKind.Recovery, .15f, motion.Facing)
            {
                Offset = _actor.GetParent<CanvasItem>().GetGlobalTransformWithCanvas()
                    .AffineInverse().BasisXform(from - CoreCanvas)
            };
            _presentations.Add(tail);
            SyncNow();
        }
        if (_actor is { Entity.IsAlive: true }) SoarSpinAnimation.EnsureAirborneSpin(_actor.Entity);
    }

    private bool ComposeTomoe(ReadOnlySpan<V2> offsets, Vector2 unposedCore, Vector2 foot,
        Transform2D parentCanvas)
    {
        if (_tomoe is not { } motion) return false;
        float time = motion.Seconds;
        float approach = TomoeThrowAnimation.Smooth(time / .05f);
        float degrees = time <= .13f ? 95f * (1f - Mathf.Pow(1f - Mathf.Clamp((time - .05f) / .08f, 0f, 1f), 3f))
            : time <= .155f ? 95f
            : 95f + 265f * Mathf.Pow(Mathf.Clamp((time - .155f) / .095f, 0f, 1f), 1.4f);
        float rotation = motion.StartAngle * (1f - approach) - motion.Facing * Mathf.DegToRad(degrees);
        motion.Angle = rotation;
        Transform2D stage = _actor!.GetParent<CanvasItem>().GetGlobalTransformWithCanvas();
        Vector2 start = stage * motion.Start;
        Vector2 target = stage * motion.Target;
        Vector2 reach = (foot - unposedCore).Rotated(-motion.Facing * Mathf.DegToRad(95f));
        float nearX = target.X - reach.X;
        float returning = .65f * TomoeThrowAnimation.Smooth((time - .25f) / .15f);
        float x = Mathf.Lerp(start.X, nearX, approach * (1f - returning));
        float ground = (parentCanvas * new Vector2(0f, NinjaSlayerFormCalibration.GroundY)).Y;
        float hop = 40f * Math.Max(0f, Mathf.Sin(Mathf.Pi * Mathf.Clamp((time - .20f) / .15f, 0f, 1f)));
        float y = ground - GroundedPoseMath.SupportY(offsets, rotation) - stage.BasisXform(new Vector2(0f, hop)).Length();
        Vector2 core = new(x, Mathf.Lerp(start.Y, y, approach));
        Transform2D delta = new(rotation, Vector2.Zero);
        delta.Origin = core - delta.BasisXform(unposedCore);
        Transform = parentCanvas.AffineInverse() * delta * parentCanvas;
        _displayAngle = rotation;
        _baseDisplayAngle = 0f;
        _effectiveTravelY = stage.AffineInverse().BasisXform(core - unposedCore).Y;
        motion.FootCanvas = delta * foot;
        SetCore(core);
        return true;
    }
}
