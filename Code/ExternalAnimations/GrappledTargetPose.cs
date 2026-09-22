using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Monsters;

namespace NinjaSlayer.Code.ExternalAnimations;

internal sealed class GrappledTargetPose : IDisposable
{
    private static readonly ConditionalWeakTable<Creature, GrappledTargetPose> Active = new();
    private readonly NCreature _actor;
    private readonly Node2D _body;
    private readonly Node2D _drawing;
    private readonly CanvasItem _parent;
    private readonly Marker2D _center;
    private readonly Transform2D _bodyBaseline;
    private readonly Transform2D _centerBaseline;
    private readonly Vector2[] _offsets;
    private readonly float _ground;
    private readonly Action? _resumeStagger;
    private MegaTrackEntry? _track;
    private readonly float _trackSpeed;
    private readonly string? _trackName;
    private readonly float _trackTime;
    private readonly FreeControlMotionBlur? _spriteBlur;
    private readonly EntangledSpinMotionBlur? _spineBlur;
    private Transform2D _lastBody;
    private Transform2D _lastCenter;

    internal bool IsActive { get; private set; } = true;
    internal Vector2 Start { get; }
    internal Vector2 Peak { get; }
    internal Vector2 Core { get; private set; }

    internal static void Release(Creature creature)
    {
        if (Active.TryGetValue(creature, out var pose)) pose.Dispose();
    }

    internal GrappledTargetPose(NCreature actor, bool blur)
    {
        Release(actor.Entity);
        _actor = actor;
        _resumeStagger = StaggerAnimation.PauseCurrent(actor.Entity);
        _drawing = actor.Visuals.GetCurrentBody();
        _body = NinjaSlayerVisualRig.GetAirborneAnchor(actor.Visuals) ?? _drawing;
        _parent = _body.GetParent<CanvasItem>();
        _center = actor.Visuals.VfxSpawnPosition;
        _bodyBaseline = _lastBody = _body.Transform;
        _centerBaseline = _lastCenter = _center.Transform;
        Start = Core = FromCanvas(_center.GetGlobalTransformWithCanvas().Origin);
        Rect2 bounds = _drawing.GetGlobalTransformWithCanvas().AffineInverse()
            * actor.Visuals.Bounds.GetGlobalTransformWithCanvas()
            * new Rect2(Vector2.Zero, actor.Visuals.Bounds.Size);
        Vector2[] points;
        if (_drawing is Sprite2D sprite && actor.Entity.Monster is SawatariMonster or DarkNinjaMonster)
        {
            string rig = actor.Entity.Monster is SawatariMonster ? "Sawatari" : "DarkNinja";
            bool standing = sprite.Texture.ResourcePath.Contains("dark_ninja_standing", StringComparison.Ordinal);
            points = ShadowBodyGeometry.Resolve(rig, false, standing).Select(p =>
            {
                Vector2 pixel = (p - Vector2.One * .5f) * sprite.Texture.GetSize();
                pixel *= new Vector2(sprite.FlipH ? -1f : 1f, sprite.FlipV ? -1f : 1f);
                return pixel + sprite.Offset;
            }).ToArray();
        }
        else
        {
            if (_drawing.IsClass(MegaSprite.spineClassName))
            {
                MegaSkeleton? skeleton = new MegaSprite(_drawing).GetSkeleton();
                using GodotObject? lease = skeleton?.BoundObject;
                if (skeleton?.GetBounds() is { } nativeBounds && nativeBounds.HasArea()) bounds = nativeBounds;
            }
            points = [bounds.Position, new(bounds.End.X, bounds.Position.Y), bounds.End, new(bounds.Position.X, bounds.End.Y)];
        }
        Transform2D toParent = _parent.GetGlobalTransformWithCanvas().AffineInverse() * _drawing.GetGlobalTransformWithCanvas();
        _offsets = points.Select(point => toParent * point - Start).ToArray();
        float authoredGround = NinjaSlayerVisualRig.GetGroundContact(actor.Visuals) is { } contact
            ? FromCanvas(contact.GetGlobalTransformWithCanvas().Origin).Y
            : FromCanvas(actor.GetGlobalTransformWithCanvas().Origin).Y;
        _ground = Math.Max(authoredGround, Start.Y + _offsets.Max(p => p.Y));
        float visibleTop = FromCanvas(new Vector2(_center.GetGlobalTransformWithCanvas().Origin.X, 215f)).Y;
        Peak = Start - Vector2.Down * Math.Min(180f, Math.Max(0f, Start.Y - visibleTop));
        if (actor.SpineAnimation.IsValid)
        {
            _track = actor.SpineAnimation.GetCurrentTrack();
            if (_track != null)
            {
                _trackSpeed = _track.BoundObject.Call("get_time_scale").AsSingle();
                _trackName = _track.GetAnimationName();
                _trackTime = _track.GetTrackTime();
                _track.SetTimeScale(0f);
            }
        }
        if (blur)
        {
            if (_drawing is Sprite2D)
            {
                _spriteBlur = new FreeControlMotionBlur { Name = "GrappleExposure", ShowBehindParent = true };
                actor.Visuals.AddChild(_spriteBlur);
            }
            else _spineBlur = EntangledSpinMotionBlur.Create(_drawing, bounds);
        }
        Active.Add(actor.Entity, this);
    }

    internal Vector2 FromCanvas(Vector2 canvas) => _parent.GetGlobalTransformWithCanvas().AffineInverse() * canvas;
    private Vector2 ToCanvas(Vector2 local) => _parent.GetGlobalTransformWithCanvas() * local;
    internal float GroundAt(float angle) => _ground - _offsets.Max(p => p.Rotated(angle).Y);

    internal void Trip(float amount, float facing)
    {
        Vector2 foot = Start + new Vector2(0f, _offsets.Max(p => p.Y));
        float angle = facing * Mathf.DegToRad(14f) * amount;
        Apply(foot + Vector2.Left * (12f * facing * amount) + (Start - foot).Rotated(angle), angle);
    }

    internal void Apply(Vector2 core, float angle)
    {
        if (!IsActive) return;
        core.Y = Math.Min(core.Y, GroundAt(angle));
        Core = core;
        Transform2D delta = new(angle, Vector2.Zero);
        delta.Origin = core - delta.BasisXform(Start);
        _body.Transform = _lastBody = delta * _bodyBaseline;
        Vector2 canvasCore = ToCanvas(core);
        _center.Position = _center.GetParent<CanvasItem>().GetGlobalTransformWithCanvas().AffineInverse() * canvasCore;
        _lastCenter = _center.Transform;
        if (_spriteBlur != null && _drawing is Sprite2D sprite)
        {
            Transform2D space = new(0f, canvasCore);
            Transform2D authored = new Transform2D(-angle, Vector2.Zero) * space.AffineInverse() * sprite.GetGlobalTransformWithCanvas();
            _spriteBlur.RecordHistory(sprite, Time.GetTicksUsec() / 1000000d, space, Vector2.Zero, authored, angle);
        }
        _spineBlur?.RecordPlanar(angle, canvasCore);
    }

    public void Dispose()
    {
        if (!IsActive) return;
        IsActive = false;
        if (Active.TryGetValue(_actor.Entity, out var currentPose) && ReferenceEquals(currentPose, this)) Active.Remove(_actor.Entity);
        _spriteBlur?.ClearHistory();
        _spriteBlur?.QueueFreeSafely();
        _spineBlur?.Stop();
        if (GodotObject.IsInstanceValid(_body) && _body.Transform.IsEqualApprox(_lastBody)) _body.Transform = _bodyBaseline;
        if (GodotObject.IsInstanceValid(_center) && _center.Transform.IsEqualApprox(_lastCenter)) _center.Transform = _centerBaseline;
        _resumeStagger?.Invoke();
        if (_track == null) return;
        if (GodotObject.IsInstanceValid(_actor) && _actor.Entity.IsAlive && _actor.SpineAnimation.IsValid)
        {
            MegaTrackEntry? current = _actor.SpineAnimation.GetCurrentTrack();
            using GodotObject? lease = current?.BoundObject;
            if (current != null && current.GetAnimationName() == _trackName && Mathf.IsEqualApprox(current.GetTrackTime(), _trackTime))
                _track.SetTimeScale(_trackSpeed);
        }
        _track.BoundObject.Dispose();
        _track = null;
    }
}
