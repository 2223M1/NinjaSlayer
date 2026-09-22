using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.Nodes;

// A single straight, finite plume per actor. Stop emission first, then let the
// remaining column leave the mouth; only detached fringe follows ballistic arcs.
internal sealed partial class NLowHealthBloodVfx : Node2D
{
    private const string Root = "res://NinjaSlayer/images/vfx/low_health_blood/";
    private const float SourceFps = 24000f / 1001f;
    private const float TravelSpeed = 3600f;
    private const float PlumeScale = .85f;
    private readonly record struct HeadSample(Vector2 Mouth, Vector2 Direction);
    private readonly record struct Spray(float Time, Vector2 Origin, Vector2 Velocity, int Frame, float Scale);
    private HeadSample _head;
    private readonly List<Spray> _spray = [];
    private NCreature _actor = null!;
    private Texture2D _jet = null!, _fringe = null!;
    private float _elapsed, _emissionEnd, _nextSpray;
    private int _sprayIndex;

    internal static bool IsLowHealth(Creature creature) => creature.GetHpPercentRemaining() <= .25d;

    internal static void Emit(Creature creature)
    {
        if (creature.IsDead || creature.GetCreatureNode() is not { } actor
            || NCombatRoom.Instance is not { } room
            || CombatActionTimingRuntime.CurrentSpeed == CombatActionSpeed.Instant) return;
        string name = "MouthBlood_" + actor.GetInstanceId();
        var effect = room.CombatVfxContainer.GetNodeOrNull<NLowHealthBloodVfx>(name);
        if (effect is { } && effect.IsQueuedForDeletion())
        {
            room.CombatVfxContainer.RemoveChild(effect);
            effect = null;
        }
        if (effect == null)
        {
            effect = new NLowHealthBloodVfx { Name = name, _actor = actor, ZIndex = 8, ProcessPriority = 150 };
            room.CombatVfxContainer.AddChild(effect);
        }
        effect._emissionEnd = effect._elapsed + .80f;
        effect.RecordHead();
        effect.QueueRedraw();
    }

    public override void _Ready()
    {
        _jet = GD.Load<Texture2D>(Root + "jet.png");
        _fringe = GD.Load<Texture2D>(Root + "spray.png");
        RenderingServer.FramePreDraw += BeforeDraw;
    }

    public override void _Process(double delta)
    {
        if (!GodotObject.IsInstanceValid(_actor) || !_actor.IsInsideTree() || _actor.Entity.IsDead)
        {
            QueueFree();
            return;
        }
        _elapsed += (float)(Engine.TimeScale > 0d ? delta / Engine.TimeScale : 0d);
        RecordHead();
        while (_nextSpray < Math.Min(_elapsed, _emissionEnd - .30f))
        {
            HeadSample head = _head;
            float spread = MathF.Sin(_sprayIndex * 2.39996f) * .25f;
            _spray.Add(new Spray(_nextSpray, head.Mouth,
                head.Direction.Rotated(spread) * (780f + (_sprayIndex % 5) * 115f),
                _sprayIndex % 4, .45f + (_sprayIndex % 3) * .12f));
            _sprayIndex++;
            _nextSpray += .045f;
        }
        _spray.RemoveAll(piece => _elapsed - piece.Time > .5f);
        if (_elapsed > _emissionEnd + .20f) QueueFree();
        QueueRedraw();
    }

    private void BeforeDraw()
    {
        if (!CanProcess() || IsQueuedForDeletion()) return;
        RecordHead();
        QueueRedraw();
    }

    private void RecordHead()
    {
        if (_elapsed >= _emissionEnd && _emissionEnd > 0f) return;
        if (!GodotObject.IsInstanceValid(_actor) || !_actor.IsInsideTree()) return;
        NinjaSlayerAimPose.Get(_actor.Entity)?.SyncNow();
        NinjaSlayerFormKind form = NinjaSlayerFormState.GetPresentation(_actor.Entity).Kind;
        Sprite2D source = NinjaSlayerVisualRig.GetBodySprite(_actor.Visuals)!;
        var overlay = _actor.Visuals.GetNodeOrNull<NarakuVisualOverlay>("AirborneAnchor/AimPose/NarakuVisualOverlay");
        Sprite2D body = overlay is { Visible: true } ? overlay : source;
        var mouth = NinjaSlayerFormCalibration.Mouth(form);
        Vector2 point = new(mouth.X, mouth.Y);
        Vector2 direction = new(1f, -.22f);
        if (body.FlipH) { point.X = -point.X; direction.X = -direction.X; }
        if (body.FlipV) { point.Y = -point.Y; direction.Y = -direction.Y; }
        if (!body.Centered) point += body.Texture.GetSize() * .5f;
        point += body.Offset;
        // The separated head uses this un-orbited source transform as well.
        // Only torso/hand tracking uses HellTornado.BodyCanvasTransform.
        Transform2D map = GetGlobalTransformWithCanvas().AffineInverse() * body.GetGlobalTransformWithCanvas();
        Vector2 heading = map.BasisXform(direction);
        if (heading.LengthSquared() < .00001f) return;
        _head = new HeadSample(map * point, heading.Normalized());
    }

    private (Vector2 Center, Vector2 Width, float Alpha) Section(float distance)
    {
        float age = distance / TravelSpeed;
        float emissionTime = _elapsed - age;
        float strength = Mathf.SmoothStep(0f, .035f, emissionTime)
            * (1f - Mathf.SmoothStep(_emissionEnd - .13f, _emissionEnd, emissionTime));
        Vector2 center = _head.Mouth + _head.Direction * distance;
        Vector2 width = -_head.Direction.Orthogonal() * (32f * PlumeScale);
        return (center, width, strength);
    }

    public override void _Draw()
    {
        if (_head.Direction == Vector2.Zero) return;
        int frame = Math.Min(23, (int)(_elapsed * SourceFps) % 24);
        Vector2 atlas = new(frame % 4 * 768f, frame / 4 * 192f);
        Vector2 size = _jet.GetSize();
        for (int segment = 0; segment < 50; segment++)
        {
            float x0 = segment * 12f, x1 = x0 + 12f;
            var a = Section(x0 * PlumeScale);
            var b = Section(x1 * PlumeScale);
            if (a.Alpha <= 0f && b.Alpha <= 0f) continue;
            DrawPolygon([a.Center-a.Width,b.Center-b.Width,b.Center+b.Width,a.Center+a.Width],
                [new(1,1,1,a.Alpha),new(1,1,1,b.Alpha),new(1,1,1,b.Alpha),new(1,1,1,a.Alpha)],
                [(atlas+new Vector2(x0+8,0))/size,(atlas+new Vector2(x1+8,0))/size,
                 (atlas+new Vector2(x1+8,192))/size,(atlas+new Vector2(x0+8,192))/size],_jet);
        }
        foreach (Spray piece in _spray)
        {
            float age = _elapsed - piece.Time;
            float alpha = 1f - Mathf.SmoothStep(.25f,.5f,age);
            Vector2 position = piece.Origin + piece.Velocity * age + new Vector2(0, 550f * age * age);
            DrawSetTransform(position,piece.Velocity.Angle(),Vector2.One * piece.Scale);
            DrawTextureRectRegion(_fringe,new Rect2(-64,-32,128,64),new Rect2(piece.Frame*128,0,128,64),new Color(1,1,1,alpha));
        }
        DrawSetTransform(Vector2.Zero);
    }

    public override void _ExitTree() => RenderingServer.FramePreDraw -= BeforeDraw;
}
