using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Orbs;
using NinjaSlayer.Content;
using NinjaSlayer.Orbs;
using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.Code.Nodes;

[GlobalClass]
public partial class ShurikenOrbVisual : Node2D
{
    internal const string GlowColorHex = "#FFB300";

    private static readonly Color GlowColor = new(GlowColorHex);
    private static readonly NodePath OverlayPath = new("AirborneAnchor/AimPose/NarakuVisualOverlay");

    private Node2D _deformedVisuals = null!;
    private Node2D _art = null!;
    private Sprite2D _edgeGlow = null!;
    private Sprite2D _body = null!;
    private Sprite2D _rearNear = null!;
    private Sprite2D _rearFar = null!;
    private Sprite2D _pulseTemplate = null!;
    private GpuParticles2D _sparks = null!;
    private NOrb _orbNode = null!;
    private Control _labelContainer = null!;
    private NCreature? _creatureNode;
    private ShurikenOrb? _orb;
    private double _breathTime;
    private float _spinDegrees;
    private float _angularSpeed;
    private bool _subscribedToFramePreDraw;

    public override void _Ready()
    {
        _deformedVisuals = GetNode<Node2D>("DeformedVisuals");
        _art = _deformedVisuals.GetNode<Node2D>("Art");
        _edgeGlow = _art.GetNode<Sprite2D>("EdgeGlow");
        _body = _art.GetNode<Sprite2D>("Body");
        _rearNear = _art.GetNode<Sprite2D>("RearNear");
        _rearFar = _art.GetNode<Sprite2D>("RearFar");
        _pulseTemplate = _art.GetNode<Sprite2D>("PulseTemplate");
        _sparks = _art.GetNode<GpuParticles2D>("Sparks");
        _orbNode = FindOrbNode();
        _orb = (ShurikenOrb)_orbNode.Model!;
        _creatureNode = FindCreatureNode();
        _labelContainer = _orbNode.GetNode<Control>("%LabelContainer");
        _labelContainer.ZIndex = 4;
#if NINJASLAYER_CHANNEL_STABLE
        _orb.Triggered += Pulse;
#else
        _orb.PassiveActivated += Pulse;
        _orb.EvokeActivated += PulseAfterEvoke;
#endif
        RenderingServer.FramePreDraw += SyncNow;
        _subscribedToFramePreDraw = true;
        SyncNow();
    }

    public override void _Process(double delta)
    {
        if (CombatActionTimingRuntime.CurrentSpeed == CombatActionSpeed.Instant
            || _orb is not { StackCount: > 0 } || _creatureNode?.Entity.IsDead == true)
            _angularSpeed = 0f;
        if (_angularSpeed != 0f)
        {
            // Integrate exponential drag exactly so the impulse is independent of frame rate.
            float decay = Mathf.Exp(-20f * (float)delta);
            _spinDegrees = Mathf.PosMod(_spinDegrees + _angularSpeed * (1f - decay) / 20f, 360f);
            _angularSpeed *= decay;
            if (Mathf.Abs(_angularSpeed) < 10f) _angularSpeed = 0f;
        }
        SyncNow();
        _breathTime += delta;
        float wave = (Mathf.Sin((float)_breathTime * 2.4f) + 1f) * 0.5f;
        _edgeGlow.Modulate = WithAlpha(GlowColor, Mathf.Lerp(0.18f, 0.34f, wave));
    }

    public override void _ExitTree()
    {
        _angularSpeed = 0f;
        if (_subscribedToFramePreDraw)
        {
            RenderingServer.FramePreDraw -= SyncNow;
            _subscribedToFramePreDraw = false;
        }

        if (_orb is null)
        {
            return;
        }
#if NINJASLAYER_CHANNEL_STABLE
        _orb.Triggered -= Pulse;
#else
        _orb.PassiveActivated -= Pulse;
        _orb.EvokeActivated -= PulseAfterEvoke;
#endif
        _orb = null;
    }

    internal void SyncNow()
    {
        if (_creatureNode != null)
            NinjaSlayerAimPose.Get(_creatureNode.Entity)?.SyncNow();
        bool hasStock = _orb is { StackCount: > 0 };
        Visible = hasStock;
        _labelContainer.Visible = hasStock;
        if (!hasStock)
        {
            _angularSpeed = 0f;
            return;
        }

        _rearNear.Visible = _orb!.StackCount >= 2;
        _rearFar.Visible = _orb.StackCount >= 3;
        _body.RotationDegrees = _edgeGlow.RotationDegrees = _spinDegrees;
        _rearNear.RotationDegrees = _spinDegrees + 12f;
        _rearFar.RotationDegrees = _spinDegrees + 24f;

        if (_creatureNode?.Entity.Player?.Character is not INinjaSlayerCharacter
            || !GodotObject.IsInstanceValid(_creatureNode)
            || !GodotObject.IsInstanceValid(_orbNode)
            || _orbNode.GetParent() is not CanvasItem orbParent)
        {
            return;
        }

        NCreatureVisuals visuals = _creatureNode.Visuals;
        Sprite2D? source = NinjaSlayerVisualRig.GetBodySprite(visuals);
        if (source == null || !GodotObject.IsInstanceValid(source))
        {
            return;
        }

        NinjaSlayerFormPresentation presentation =
            NinjaSlayerFormState.GetPresentation(_creatureNode.Entity);
        Sprite2D? overlay = visuals.GetNodeOrNull<Sprite2D>(OverlayPath);
        Sprite2D body = overlay != null
            && GodotObject.IsInstanceValid(overlay)
            && (presentation.UsesOverlay || overlay.Visible)
                ? overlay
                : source;

        Transform2D bodyCanvas = NinjaSlayerHellTornadoVisual.Get(_creatureNode.Entity) is { Active: true } tornado
            ? tornado.BodyCanvasTransform : body.GetGlobalTransformWithCanvas();
        Transform2D parentCanvas = orbParent.GetGlobalTransformWithCanvas();
        Transform2D visualsCanvas = visuals.GetGlobalTransformWithCanvas();
        if (Mathf.IsZeroApprox(parentCanvas.Determinant())
            || Mathf.IsZeroApprox(visualsCanvas.Determinant()))
        {
            return;
        }

        Vector2 handPoint = ResolveHandPoint(body, presentation.Kind);
        Vector2 handCanvas = bodyCanvas * handPoint;
        Vector2 orbPosition = parentCanvas.AffineInverse() * handCanvas;
        if (!orbPosition.IsFinite())
        {
            return;
        }

        _orbNode.Position = orbPosition;

        float authoredScale = NinjaSlayerFormCalibration.For(presentation.Kind).Scale;
        if (authoredScale <= 0f)
        {
            return;
        }

        Transform2D relative = visualsCanvas.AffineInverse() * bodyCanvas;
        Vector2 x = relative.X / authoredScale;
        Vector2 y = relative.Y / authoredScale;
        if (body.FlipH)
        {
            x = -x;
        }
        if (body.FlipV)
        {
            y = -y;
        }
        if (x.IsFinite() && y.IsFinite())
        {
            _deformedVisuals.Transform = new Transform2D(x, y, Vector2.Zero);
        }
    }

    internal void OnShurikenReleased(float projectileCanvasHandedness)
    {
        SyncNow();
        if (!Visible || _creatureNode?.Entity.IsDead == true) return;
        float handedness = _body.GetGlobalTransformWithCanvas().Determinant() < 0f ? -1f : 1f;
        float direction = handedness * projectileCanvasHandedness;
        if (CombatActionTimingRuntime.CurrentSpeed == CombatActionSpeed.Instant)
        {
            _angularSpeed = 0f;
            _spinDegrees = Mathf.PosMod(_spinDegrees + direction * 60f, 360f);
            SyncNow();
            return;
        }
        _angularSpeed = Mathf.Clamp(_angularSpeed + direction * 1200f, -1800f, 1800f);
    }

    private NOrb FindOrbNode()
    {
        for (Node? node = GetParent(); node is not null; node = node.GetParent())
        {
            if (node is NOrb { Model: ShurikenOrb })
            {
                return (NOrb)node;
            }
        }

        throw new InvalidOperationException("Shuriken orb visuals must be parented under their NOrb.");
    }

    private NCreature? FindCreatureNode()
    {
        for (Node? node = _orbNode.GetParent(); node is not null; node = node.GetParent())
        {
            if (node is NCreature creatureNode)
            {
                return creatureNode;
            }
        }

        return null;
    }

    internal static bool TryGetHandCanvasPosition(NCreature creature, out Vector2 position)
    {
        position = default;
        if (creature.Entity.Player?.Character is not INinjaSlayerCharacter) return false;
        NinjaSlayerAimPose.Get(creature.Entity)?.SyncNow();
        NinjaSlayerFormPresentation form = NinjaSlayerFormState.GetPresentation(creature.Entity);
        Sprite2D? overlay = creature.Visuals.GetNodeOrNull<Sprite2D>(OverlayPath);
        Sprite2D? body = overlay != null && (form.UsesOverlay || overlay.Visible)
            ? overlay : NinjaSlayerVisualRig.GetBodySprite(creature.Visuals);
        if (body == null || !GodotObject.IsInstanceValid(body)) return false;
        Transform2D bodyCanvas = NinjaSlayerHellTornadoVisual.Get(creature.Entity) is { Active: true } tornado
            ? tornado.BodyCanvasTransform : body.GetGlobalTransformWithCanvas();
        position = bodyCanvas * ResolveHandPoint(body, form.Kind);
        return true;
    }

    private static Vector2 ResolveHandPoint(
        Sprite2D body,
        NinjaSlayerFormKind formKind)
    {
        var hand = NinjaSlayerFormCalibration.For(formKind).Hand;
        Vector2 point = new(hand.X, hand.Y);

        if (body.FlipH)
        {
            point.X = -point.X;
        }
        if (body.FlipV)
        {
            point.Y = -point.Y;
        }
        if (!body.Centered && body.Texture is { } texture)
        {
            point += texture.GetSize() * 0.5f;
        }

        return point + body.Offset;
    }

#if !NINJASLAYER_CHANNEL_STABLE
    private void PulseAfterEvoke(Creature[] _) => Pulse();
#endif

    private void Pulse()
    {
        var pulse = (Sprite2D)_pulseTemplate.Duplicate();
        pulse.Name = "ActivationPulse";
        pulse.Visible = true;
        pulse.Scale = Vector2.One * 0.72f;
        pulse.Modulate = WithAlpha(GlowColor, 0.95f);
        _art.AddChild(pulse);

        Tween tween = pulse.CreateTween().SetParallel();
        tween.TweenProperty(pulse, "scale", Vector2.One * 1.55f, 0.28f)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Quad);
        tween.TweenProperty(pulse, "modulate:a", 0f, 0.28f)
            .SetEase(Tween.EaseType.Out);
        tween.Chain().TweenCallback(Callable.From(pulse.QueueFree));

        _sparks.Restart();
    }

    private static Color WithAlpha(Color color, float alpha) =>
        new(color.R, color.G, color.B, alpha);
}
