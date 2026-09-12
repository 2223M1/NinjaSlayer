using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Lifecycle;
using NinjaSlayer.Content;
using V2 = System.Numerics.Vector2;

namespace NinjaSlayer.Code.Nodes;

[GlobalClass]
public partial class NinjaSlayerAimPose : Node2D
{
    private NCreature? _actor;
    private Node2D _airborne = null!;
    private Marker2D _center = null!;
    private Transform2D _centerBaseline;
    private readonly V2[] _offsets = new V2[64];
    private Creature? _target;
    private Vector2? _finisherTargetLocal;
    private Node? _dragOwner;
    private CardPlay? _preparedKick;
    private Vector2 _pointer;
    private Creature? _hovered;
    private bool _busy;
    private bool _exclusive;
    private bool _returning;
    private bool _enabled;
    private bool _subscribed;
    private bool? _dragFacingBaseline;
    private float _angle;
    private float _displayAngle;
    private float _actionStartAngle;
    private float _actionBlend = 1f;
    private float _kick;
    private float _returnAngle;
    private float _returnKick;
    private float _returnLaunch;
    private Vector2 _returnTravel;
    private Vector2 _travel;
    private float _effectiveTravelY;
    private Vector2? _contactOffset;
    private float _chargeSeconds;
    private float _chargeBack;
    private float _returnBack;
    private float _launch;
    private bool _charging;
    private bool _tornado;
    private bool _tornadoEmpowered;
    private float _spinPauseRemaining;
    private float _spinDegrees;
    private Func<double, double>? _spinExposure;
    private VerticalAxisSpinProjection? _spin;
    private Tween? _poseTween;
    private long _generation;
    private RapidMotionChannel[] _exclusiveChannels = [];
    private Vector2[] _exclusiveReturnStarts = [];

    internal void OwnAirborneReturn(RapidMotionChannel[] channels) => _exclusiveChannels = channels;

    internal static NinjaSlayerAimPose? Get(Creature creature) =>
        creature.GetCreatureNode()?.Visuals.GetNodeOrNull<NinjaSlayerAimPose>("%AimPose");

    internal static bool IsKick(CardModel? card) => card is SatsubatsuRedesignV1
        or OneDrinkOneStrikeRedesignV1 or RoundhouseKickRedesignV1
        or SweepKickRedesignV1 or DragonFlyingKickRedesignV1;

    internal static Creature? Focus(Creature actor) => actor.CombatState?.GetOpponentsOf(actor)
        .Where(c => c.IsAlive && c.IsHittable && c.GetCreatureNode() != null)
        .OrderByDescending(c => c.IsPrimaryEnemy)
        .ThenBy(c => c.GetCreatureNode()!.GlobalPosition.X)
        .FirstOrDefault();

    internal Vector2 Travel => _travel + Vector2.Right * _chargeBack;
    internal bool IsTornado => _tornado;
    internal bool IsEmpoweredTornado => _tornado && _tornadoEmpowered;
    internal bool UseTornadoHitStop { get; set; } = true;
    internal bool OwnsSpin => _spin != null || Turning;
    internal bool IsAiming => _enabled;
    internal bool IsExclusive => _exclusive;
    internal bool IsBusy => _busy || _exclusive;
    internal float? AttackForwardSign { get; set; }
    internal float VerticalTravel => _effectiveTravelY;
    internal Vector2 CoreCanvas => _center.GetGlobalTransformWithCanvas().Origin;

    public override void _Ready()
    {
        _airborne = GetParent<Node2D>();
        _center = GetNode<Marker2D>("%CenterPos");
        _centerBaseline = _center.Transform;
        for (Node? node = GetParent(); node != null; node = node.GetParent())
            if (node is NCreature actor) { _actor = actor; break; }
        ProcessPriority = 90;
        RenderingServer.FramePreDraw += SyncNow;
        _subscribed = true;
        SyncNow();
    }

    public override void _ExitTree()
    {
        if (_subscribed)
            RenderingServer.FramePreDraw -= SyncNow;
        _subscribed = false;
        ClearDragPresentation();
        ClearPresentation();
        StopPoseTween();
        _spin?.Restore();
        _spin = null;
    }

    public override void _Process(double delta)
    {
        if (_busy && !_exclusive) _actionBlend = Math.Min(1f, _actionBlend + (float)delta / 0.05f);
        if (_actor?.Entity.IsDead == true)
        {
            if (_enabled || _spin != null || HasPresentation) Reset();
            return;
        }
        AdvancePresentation((float)delta);
        AdvanceTurn((float)delta);
        if (_dragOwner != null && !GodotObject.IsInstanceValid(_dragOwner))
            EndDrag(_dragOwner, false);
        UpdateTornadoCharge();
        if (_charging && !_busy && !_exclusive)
            AdvanceCharge((float)delta);
        else if (_tornado)
        {
            float speed = _tornadoEmpowered ? 12000f
                : CombatActionTimingRuntime.CurrentSpeed == CombatActionSpeed.Fast ? 4800f : 2400f;
            double stopped = Math.Min(delta, _spinPauseRemaining);
            _spinPauseRemaining = (float)Math.Max(0d, _spinPauseRemaining - stopped);
            double from = _spinDegrees;
            _spinDegrees += speed * (float)(delta - stopped);
            _spinExposure = age => from + speed * Math.Clamp(delta - age - stopped, 0d, delta - stopped);
        }
        else _spinExposure = null;
        SyncNow();
    }

    private float FacingSign => _airborne.Scale.X < 0f ? -1f : 1f;

    internal void Drag(Node owner, CardModel card, Vector2 pointerCanvas, Creature? hovered)
    {
        if (_dragOwner == null)
        {
            _dragFacingBaseline = FacingSign < 0f;
            if (_returning && !IsBusy) { StopPoseTween(); _returning = false; }
        }
        _dragOwner = owner;
        _dragCard = card;
        _pointer = pointerCanvas;
        _hovered = hovered;
        if (IsBusy) return;
        _enabled = true;
        if (card is TornadoFistRedesignV1)
        {
            UpdateTornadoCharge();
        }
        else if (_actor != null)
        {
            float targetX = hovered?.GetCreatureNode()?.Visuals.VfxSpawnPosition.GetGlobalTransformWithCanvas().Origin.X
                ?? pointerCanvas.X;
            // Mirroring moves the off-center hit marker across the root. Using that
            // marker here makes a stationary overhead pointer flip the body every frame.
            float side = targetX - _airborne.GetGlobalTransformWithCanvas().Origin.X;
            bool left = Mathf.IsZeroApprox(side) ? FacingSign < 0f : side < 0f;
            RequestTurn(left);
        }
        SyncNow();
    }

    internal void EndDrag(Node owner, bool played)
    {
        if (!ReferenceEquals(owner, _dragOwner)) return;
        _dragOwner = null;
        _dragCard = null;
        _hovered = null;
        if (!played && !IsBusy && _actor != null && _dragFacingBaseline is { } left)
            RequestTurn(left);
        _dragFacingBaseline = null;
        if (IsBusy) return;
        if (played && _charging)
        {
            // Preserve the charged frame until the queued card claims it. There is no
            // gameplay charge duration, and cancellation/room cleanup still owns it.
            _charging = false;
            _chargePending = true;
            return;
        }
        _charging = false;
        BeginReturn();
        _ = ReturnPreview();
    }

    private async Task ReturnPreview()
    {
        await TweenPose(0.2f, ApplyReturn);
    }

    internal void ReleaseEmptyTornado()
    {
        if (IsBusy || _dragOwner != null || !HasCharge && _spin == null) return;
        BeginReturn();
        _ = ReturnPreview();
    }

    internal void BeginAction(Creature? target, bool exclusive = false, bool preserveTornado = false)
    {
        SyncNow();
        if (exclusive)
        {
            AttackForwardSign = null;
            ClearPresentation();
        }
        StopPoseTween();
        _actionStartAngle = _baseDisplayAngle;
        _actionBlend = exclusive ? 1f : 0f;
        _target = target ?? (_actor != null ? Focus(_actor.Entity) : null);
        _finisherTargetLocal = exclusive && _target?.GetCreatureNode() is { } focus
            ? _actor!.GetParent<CanvasItem>().GetGlobalTransformWithCanvas().AffineInverse()
                * focus.Visuals.VfxSpawnPosition.GetGlobalTransformWithCanvas().Origin
            : null;
        _busy = true;
        _exclusive = exclusive;
        _returning = false;
        _enabled = true;
        _contactOffset = null;
        _charging = false;
        _chargePending = false;
        _chargeCard = null;
        _chargeActionScale = _chargeScale;
        if (!preserveTornado && _spin != null)
        {
            _travel.Y = _baseEffectiveTravelY;
            _angle *= 1f - _launch;
            StopSpin();
        }
        if (_actor != null && _target?.GetCreatureNode() is { } node)
            FaceForAction(AttackForwardSign is { } forward
                ? forward < 0f : node.GlobalPosition.X < _actor.GlobalPosition.X, exclusive);
        SyncNow();
    }

    internal async Task PrepareKick(CardPlay? play)
    {
        if (play == null || !IsKick(play.Card))
        {
            _kick = 0f;
            SyncNow();
            return;
        }
        if (ReferenceEquals(_preparedKick, play))
        {
            _kick = 1f;
            return;
        }
        _preparedKick = play;
        float from = _kick;
        float seconds = CombatActionTimingRuntime.Resolve(0.05f, 0.025f);
        if (seconds <= 0f) _actionBlend = 1f;
        await TweenPose(seconds, p =>
        {
            _actionBlend = Math.Max(_actionBlend, p);
            _kick = Mathf.Lerp(from, 1f, p);
            SyncNow();
        });
    }

    internal void BeginTornado(Creature? target, bool exclusive = false, bool empowered = false)
    {
        _tornado = true;
        _tornadoEmpowered = empowered;
        _spinPauseRemaining = 0f;
        _launch = exclusive ? 1f : 0f;
        BeginAction(target, exclusive, preserveTornado: true);
        _kick = 0f;
        StartSpin();
    }

    internal void PauseTornadoSpin(float seconds)
    {
        if (_tornado && !_exclusive) _spinPauseRemaining = Math.Max(_spinPauseRemaining, seconds);
    }

    internal void PlaceAtImpact(Creature target, float impactRootX, bool moveRoot = true)
    {
        if (_actor == null) return;
        Vector2 incoming = (TargetCanvas() - CoreCanvas).Normalized();
        if (incoming.LengthSquared() < 0.001f) incoming = Vector2.Right * FacingSign;
        // Discard the preceding visual lunge before placing the actor at impact.
        _travel.X = 0f;
        _chargeBack = 0f;
        SyncNow();
        CanvasItem parent = _actor.GetParent<CanvasItem>();
        Transform2D parentCanvas = parent.GetGlobalTransformWithCanvas();
        Vector2 targetCanvas = target.GetCreatureNode()!.Visuals.VfxSpawnPosition.GetGlobalTransformWithCanvas().Origin;
        Vector2 rootShift = parentCanvas.BasisXform(new(impactRootX - _actor.Position.X, 0f));
        float separation = Math.Abs(targetCanvas.X - (CoreCanvas.X + rootShift.X));
        _contactOffset = parentCanvas.AffineInverse().BasisXform(
            -incoming * (separation / Math.Max(0.15f, Math.Abs(incoming.X))));
        if (moveRoot)
            _actor.Position = new(impactRootX, _actor.Position.Y);
        else
            _actor.Visuals.Position += _actor.Visuals.GetParent<CanvasItem>()
                .GetGlobalTransformWithCanvas().AffineInverse().BasisXform(rootShift);
        SyncNow();
    }

    private void StartSpin()
    {
        if (_spin != null || _actor == null) return;
        SoarSpinAnimation.SuspendForCinematic(_actor.Entity);
        Sprite2D body = NinjaSlayerVisualRig.GetBodySprite(_actor.Visuals)!;
        Transform2D old = Transform;
        Transform = Transform2D.Identity;
        Node2D marker = NinjaSlayerVisualRig.GetCinematicFocus(_actor.Visuals)!;
        Vector2 pivot = marker.GetGlobalTransformWithCanvas().Origin;
        _spin = VerticalAxisSpinProjection.CaptureCurrent(body, pivot.X, pivot);
        _spinDegrees = 0f;
        _spinExposure = null;
        _spin.ApplyDegrees(0f);
        Transform = old;
    }

    private void StopSpin()
    {
        _spin?.Restore();
        _spin = null;
        _spinExposure = null;
        _charging = false;
        _tornado = false;
        _tornadoEmpowered = false;
        _spinPauseRemaining = 0f;
        _launch = 0f;
        if (_actor != null && !_actor.Entity.IsDead)
            SoarSpinAnimation.EnsureAirborneSpin(_actor.Entity);
    }

    internal Vector2 DirectionLocal()
    {
        SyncNow();
        Vector2 direction = TargetCanvas() - CoreCanvas;
        if (direction.LengthSquared() < 0.001f) direction = Vector2.Right * FacingSign;
        return _actor!.GetParent<CanvasItem>().GetGlobalTransformWithCanvas()
            .AffineInverse().BasisXform(direction).Normalized();
    }

    internal void SetTravel(Vector2 offset, float launchProgress = 1f)
    {
        if (_actor == null || _exclusive) return;
        if (launchProgress >= 1f)
        {
            _actionBlend = 1f;
            if (Turning) FinishTurn(_turnTo > 90f);
        }
        _chargeScale = _chargeActionScale.Lerp(Vector2.One, Mathf.Clamp(launchProgress, 0f, 1f));
        _travel = offset;
        _chargeBack = 0f;
        if (_tornado) _launch = launchProgress;
        SyncNow();
    }

    internal void BeginReturn()
    {
        StopPoseTween();
        _busy = false;
        AttackForwardSign = null;
        _exclusive = false;
        _returning = true;
        _charging = false;
        _chargePending = false;
        _chargeCard = null;
        _returnChargeScale = _chargeScale;
        _returnAngle = _baseDisplayAngle;
        _returnKick = _kick;
        _returnLaunch = _launch;
        _returnTravel = _travel;
        _returnTravel.Y = _baseEffectiveTravelY;
        _contactOffset = null;
        _returnBack = _chargeBack;
        _exclusiveReturnStarts = _exclusiveChannels.Select(channel => channel.GetPosition()).ToArray();
    }

    internal void ApplyReturn(float progress)
    {
        float p = Mathf.Clamp(progress, 0f, 1f);
        _travel = _returnTravel * (1f - p);
        _chargeBack = _returnBack * (1f - p);
        _chargeScale = _returnChargeScale.Lerp(Vector2.One, p);
        _kick = _returnKick * (1f - p);
        _launch = _returnLaunch * (1f - p);
        _angle = _returnAngle * (1f - p);
        for (int i = 0; i < _exclusiveChannels.Length; i++)
            if (GodotObject.IsInstanceValid(_exclusiveChannels[i].Target))
                _exclusiveChannels[i].SetPosition(_exclusiveReturnStarts[i].Lerp(_exclusiveChannels[i].Baseline, p));
        SyncNow();
        if (p >= 1f)
        {
            if (Turning) FinishTurn(_turnTo > 90f);
            StopSpin();
            _finisherTargetLocal = null;
            _returning = false;
            _enabled = _dragOwner != null && (_dragCard is not TornadoFistRedesignV1 tornado || tornado.ShouldCharge);
            if (!_enabled) Transform = Transform2D.Identity;
            SyncNow();
        }
    }

    internal void Reset()
    {
        ClearDragPresentation();
        if (_actor != null) NinjaSlayerSpinMotionBlur.Get(_actor.Entity)?.Reset();
        ClearPresentation();
        foreach (RapidMotionChannel channel in _exclusiveChannels)
            if (GodotObject.IsInstanceValid(channel.Target)) channel.SetPosition(channel.Baseline);
        _exclusiveChannels = [];
        _exclusiveReturnStarts = [];
        StopPoseTween();
        StopSpin();
        _dragOwner = null;
        _target = null;
        _finisherTargetLocal = null;
        _dragFacingBaseline = null;
        _hovered = null;
        _preparedKick = null;
        AttackForwardSign = null;
        _busy = _exclusive = _returning = _enabled = false;
        _travel = Vector2.Zero;
        _effectiveTravelY = 0f;
        _contactOffset = null;
        _angle = _kick = _chargeBack = 0f;
        _displayAngle = 0f;
        Transform = Transform2D.Identity;
        if (GodotObject.IsInstanceValid(_center)) _center.Transform = _centerBaseline;
    }

    internal async Task BlendToNeutral(float seconds)
    {
        BeginReturn();
        _exclusive = true;
        await TweenPose(seconds, ApplyReturn);
    }

    private void StopPoseTween()
    {
        _generation++;
        if (_poseTween is { } tween && tween.IsValid()) tween.Kill();
        _poseTween = null;
    }

    private async Task TweenPose(float seconds, Action<float> apply)
    {
        StopPoseTween();
        long generation = _generation;
        if (seconds <= 0f) { apply(1f); return; }
        Tween tween = CreateTween();
        _poseTween = tween;
        tween.TweenMethod(Callable.From<float>(p =>
        {
            if (generation == _generation) apply(Mathf.SmoothStep(0f, 1f, p));
        }), 0f, 1f, seconds);
        if (await TweenPlayback.AwaitCompletion(tween, this) && generation == _generation)
        {
            _poseTween = null;
            apply(1f);
        }
    }

    private Vector2 TargetCanvas()
    {
        // The cinematic still owns its victim after lethal damage. Keep that
        // focus through death/removal; a fallback relative to our moving core
        // would feed the contact pose back into itself on every render frame.
        if (_finisherTargetLocal is { } lastTarget)
        {
            Transform2D parentCanvas = _actor!.GetParent<CanvasItem>().GetGlobalTransformWithCanvas();
            if (_target?.GetCreatureNode() is { } victim && GodotObject.IsInstanceValid(victim)
                && !victim.IsQueuedForDeletion())
            {
                Vector2 victimCenter = victim.Visuals.VfxSpawnPosition.GetGlobalTransformWithCanvas().Origin;
                _finisherTargetLocal = parentCanvas.AffineInverse() * victimCenter;
                return victimCenter;
            }
            return parentCanvas * lastTarget;
        }
        bool preview = !_busy && !_exclusive && _dragOwner != null;
        Creature? target = preview ? _hovered : _target;
        if (target?.IsAlive == true && target.GetCreatureNode() is { } targetNode)
            return ForwardTarget(targetNode.Visuals.VfxSpawnPosition.GetGlobalTransformWithCanvas().Origin);
        if (preview) return _pointer;
        if (_actor != null && (_target = Focus(_actor.Entity))?.GetCreatureNode() is { } focus)
            return ForwardTarget(focus.Visuals.VfxSpawnPosition.GetGlobalTransformWithCanvas().Origin);
        return CoreCanvas + Vector2.Right * FacingSign * 100f;
    }

    private Vector2 ForwardTarget(Vector2 target)
    {
        if (_busy && !_exclusive && AttackForwardSign is { } direction)
            target.X = CoreCanvas.X + direction * Math.Abs(target.X - CoreCanvas.X);
        return target;
    }

    internal void SyncNow()
    {
        if (_actor == null || !GodotObject.IsInstanceValid(_actor) || _actor.Entity == null || _actor.Visuals == null || _actor.Entity.IsDead
            || !GodotObject.IsInstanceValid(_center)) return;
        _spin?.ApplyDegrees(_spinDegrees, _spinExposure);
        ApplyTurnProjection();
        Sprite2D source = NinjaSlayerVisualRig.GetBodySprite(_actor.Visuals)!;
        NarakuVisualOverlay overlay = GetNode<NarakuVisualOverlay>("NarakuVisualOverlay");
        overlay.SyncForPose();
        Sprite2D body = overlay.Visible ? overlay : source;
        bool full = NinjaSlayerFormState.GetPresentation(_actor.Entity).Kind == NinjaSlayerFormKind.FullyReleasedNaraku;
        V2[] contour = full ? CombatBodyContours.FullyReleasedNaraku : CombatBodyContours.NinjaSlayer;
        Vector2 corePoint = SpritePoint(body, full ? new(50.8f, 120f) : new(580f, 30.30303f));
        Vector2 footPoint = SpritePoint(body, full ? new(422f, 497f) : new(295f, 535f));
        Transform2D parentCanvas = _airborne.GetGlobalTransformWithCanvas();
        if (Mathf.IsZeroApprox(parentCanvas.Determinant())) return;
        Transform2D unposedBody = parentCanvas * body.Transform;
        Vector2 core = unposedBody * corePoint;
        float top = float.PositiveInfinity, bottom = float.NegativeInfinity;
        for (int i = 0; i < contour.Length; i++)
        {
            Vector2 point = unposedBody * SpritePoint(body, new(contour[i].X, contour[i].Y)) - core;
            point *= _chargeScale;
            _offsets[i] = new(point.X, point.Y);
            top = Math.Min(top, point.Y);
            bottom = Math.Max(bottom, point.Y);
        }
        _bodyHeight = bottom - top;
        ReadOnlySpan<V2> offsets = _offsets.AsSpan(0, contour.Length);
        if (!_enabled)
        {
            _effectiveTravelY = 0f;
            Vector2 presentedCore = core;
            float presentedRotation = 0f;
            ComposePresentation(offsets, ref presentedRotation, ref presentedCore);
            Transform2D presentation = new(presentedRotation, Vector2.Zero);
            presentation.Origin = presentedCore - presentation.BasisXform(core);
            Transform = parentCanvas.AffineInverse() * presentation * parentCanvas;
            _displayAngle = presentedRotation;
            SetCore(presentedCore);
            return;
        }
        float standingY = full ? 15.5f : -6.45f;
        float altitude = Math.Max(0f, -_airborne.Position.Y);
        float travelY = GroundedPoseMath.ClampDescent(_travel.Y, altitude);
        Vector2 travelCanvas = _actor.GetParent<CanvasItem>().GetGlobalTransformWithCanvas().BasisXform(new(_travel.X + _chargeBack, travelY));
        core += travelCanvas;
        float line = (parentCanvas * new Vector2(0f, standingY)).Y + travelCanvas.Y;
        float originalLine = line;
        Vector2 target = TargetCanvas();
        if (_tornado)
            line = Mathf.Lerp(line, target.Y, _launch);
        _effectiveTravelY = travelY + _actor.GetParent<CanvasItem>().GetGlobalTransformWithCanvas()
            .AffineInverse().BasisXform(new Vector2(0f, line - originalLine)).Y;
        float reference = FacingSign < 0f ? Mathf.Pi : 0f;
        Vector2 footDirection = (unposedBody * footPoint - (core - travelCanvas)) * _chargeScale;
        float feetAngle = footDirection.Angle();
        reference += GroundedPoseMath.WrapAngle(feetAngle - reference) * _kick;
        if (!_returning || _dragOwner != null && !_exclusive)
            _angle = ChargePreview ? 0f
                : !IsBusy && _dragOwner != null ? PreviewAngle(offsets, core, target, line, reference)
                : GroundedPoseMath.AimAngle(offsets, new(core.X, core.Y), new(target.X, target.Y), line, reference, _angle);
        float rotation = _angle * (_tornado ? 1f - _launch : 1f);
        if (_busy && !_exclusive && !_tornado)
            rotation = Mathf.LerpAngle(_actionStartAngle, rotation, Mathf.SmoothStep(0f, 1f, _actionBlend));
        float support = GroundedPoseMath.SupportY(offsets, rotation);
        if (_tornado && _launch > 0f)
            support = Mathf.Lerp(support, footDirection.Rotated(rotation).Y, _launch);
        Vector2 finalCore = new(core.X, line - support);
        if (_exclusive && _contactOffset is { } localContact && !_tornado)
        {
            Vector2 contact = _actor.GetParent<CanvasItem>().GetGlobalTransformWithCanvas().BasisXform(localContact);
            finalCore = target + contact;
            for (int i = 0; i < 64; i++)
            {
                finalCore.Y = Math.Min(target.Y + contact.Y, originalLine - GroundedPoseMath.SupportY(offsets, rotation));
                float desired = GroundedPoseMath.WrapAngle((target - finalCore).Angle() - reference);
                float change = GroundedPoseMath.WrapAngle(desired - rotation);
                rotation += change * 0.5f;
                if (Math.Abs(change) < 0.00001f) break;
            }
            float lowest = GroundedPoseMath.SupportY(offsets, rotation);
            finalCore.Y = Math.Min(target.Y + contact.Y, originalLine - lowest);
            _angle = rotation;
            _effectiveTravelY = _actor.GetParent<CanvasItem>().GetGlobalTransformWithCanvas()
                .AffineInverse().BasisXform(finalCore - new Vector2(finalCore.X, originalLine - lowest)).Y;
        }
        ComposePresentation(offsets, ref rotation, ref finalCore);
        Transform2D worldRotation = new(rotation, _chargeScale, 0f, Vector2.Zero);
        _displayAngle = rotation;
        worldRotation.Origin = finalCore - worldRotation.BasisXform(core - travelCanvas);
        Transform = parentCanvas.AffineInverse() * worldRotation * parentCanvas;
        SetCore(finalCore);
    }

    private void SetCore(Vector2 canvas) => _center.Position = _center.GetParent<CanvasItem>()
        .GetGlobalTransformWithCanvas().AffineInverse() * canvas;

    private static Vector2 SpritePoint(Sprite2D sprite, Vector2 centeredPoint)
    {
        if (sprite.FlipH) centeredPoint.X = -centeredPoint.X;
        if (sprite.FlipV) centeredPoint.Y = -centeredPoint.Y;
        if (!sprite.Centered && sprite.Texture != null) centeredPoint += sprite.Texture.GetSize() * 0.5f;
        return centeredPoint + sprite.Offset;
    }
}
