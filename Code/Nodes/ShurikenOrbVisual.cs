using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Orbs;
using NinjaSlayer.Content;
using NinjaSlayer.Orbs;

namespace NinjaSlayer.Code.Nodes;

[GlobalClass]
public partial class ShurikenOrbVisual : Node2D
{
    internal const string GlowColorHex = "#FFB300";

    private static readonly Color GlowColor = new(GlowColorHex);
    private static readonly Vector2 NormalHandAnchor = new(851.5152f, -75.75758f);
    private static readonly Vector2 FullyReleasedHandAnchor = new(432f, 390f);
    private static readonly NodePath OverlayPath = new("AirborneAnchor/NarakuVisualOverlay");

    private Node2D _deformedVisuals = null!;
    private Node2D _art = null!;
    private Sprite2D _edgeGlow = null!;
    private Sprite2D _pulseTemplate = null!;
    private GpuParticles2D _sparks = null!;
    private NOrb _orbNode = null!;
    private Control _labelContainer = null!;
    private NCreature? _creatureNode;
    private ShurikenOrb? _orb;
    private double _breathTime;
    private bool _subscribedToFramePreDraw;

    public override void _Ready()
    {
        _deformedVisuals = GetNode<Node2D>("DeformedVisuals");
        _art = _deformedVisuals.GetNode<Node2D>("Art");
        _edgeGlow = _art.GetNode<Sprite2D>("EdgeGlow");
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
        SyncNow();
        _breathTime += delta;
        float wave = (Mathf.Sin((float)_breathTime * 2.4f) + 1f) * 0.5f;
        _edgeGlow.Modulate = WithAlpha(GlowColor, Mathf.Lerp(0.18f, 0.34f, wave));
    }

    public override void _ExitTree()
    {
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
        bool hasStock = _orb is { StackCount: > 0 };
        Visible = hasStock;
        _labelContainer.Visible = hasStock;
        if (!hasStock)
        {
            return;
        }

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

        Transform2D bodyCanvas = body.GetGlobalTransformWithCanvas();
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

        float authoredScale = ResolveAuthoredScale(presentation.Kind);
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

    private static Vector2 ResolveHandPoint(
        Sprite2D body,
        NinjaSlayerFormKind formKind)
    {
        Vector2 point = formKind switch
        {
            NinjaSlayerFormKind.Normal or NinjaSlayerFormKind.Naraku => NormalHandAnchor,
            NinjaSlayerFormKind.FullyReleasedNaraku => FullyReleasedHandAnchor,
            _ => throw new ArgumentOutOfRangeException(nameof(formKind), formKind, null)
        };

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

    private static float ResolveAuthoredScale(
        NinjaSlayerFormKind formKind)
    {
        return formKind switch
        {
            NinjaSlayerFormKind.Normal or NinjaSlayerFormKind.Naraku =>
                NinjaSlayerCombatVisuals.BodySpriteBaseScale,
            NinjaSlayerFormKind.FullyReleasedNaraku => 0.5f,
            _ => throw new ArgumentOutOfRangeException(nameof(formKind), formKind, null)
        };
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
