using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace NinjaSlayer.Code.ExternalAnimations;

internal sealed class FinisherImpactPresentation : IDisposable
{
    private const int CanvasLayerIndex = 90;
    private const float MaximumBackdropAlpha = 0.55f;

    private const string ImpactShaderCode = """
        shader_type canvas_item;
        render_mode unshaded, blend_add;

        uniform vec2 impact_uv = vec2(0.5, 0.5);
        uniform float aspect_ratio = 1.7777778;
        uniform float intensity = 0.0;
        uniform float flash = 0.0;

        void fragment() {
            vec2 delta = UV - impact_uv;
            delta.x *= aspect_ratio;
            float distance_from_impact = length(delta);
            float angle = atan(delta.y, delta.x);
            float spoke = pow(max(0.0, sin(angle * 22.0 + distance_from_impact * 34.0)), 14.0);
            float rays = spoke * (1.0 - smoothstep(0.035, 0.92, distance_from_impact));
            float ring = 1.0 - smoothstep(0.035, 0.08, abs(distance_from_impact - 0.105));
            float core = 1.0 - smoothstep(0.0, 0.09, distance_from_impact);
            float alpha = intensity * (rays * 0.42 + ring * 0.34 + core * 0.38)
                + flash * core * 0.78;
            vec3 crimson = vec3(0.84, 0.035, 0.06);
            vec3 color = mix(crimson, vec3(1.0), clamp(flash * core, 0.0, 1.0));
            COLOR = vec4(color * alpha, alpha);
        }
        """;

    private static readonly StringName ImpactUvParameter = new("impact_uv");
    private static readonly StringName AspectRatioParameter = new("aspect_ratio");
    private static readonly StringName IntensityParameter = new("intensity");
    private static readonly StringName FlashParameter = new("flash");

    private readonly NCombatRoom _room;
    private readonly CanvasLayer _layer;
    private readonly ColorRect _backdrop;
    private readonly List<ShaderMaterial> _impactMaterials = [];
    private bool _disposed;

    private FinisherImpactPresentation(NCombatRoom room, int impactCount)
    {
        _room = room;
        _layer = new CanvasLayer
        {
            Name = "NinjaSlayerFinisherImpactLayer",
            Layer = CanvasLayerIndex
        };
        _backdrop = new ColorRect
        {
            Name = "Backdrop",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Color = Colors.Transparent
        };
        _backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(_backdrop);

        for (int i = 0; i < impactCount; i++)
        {
            var shader = new Shader { Code = ImpactShaderCode };
            var material = new ShaderMaterial { Shader = shader };
            var rays = new ColorRect
            {
                Name = $"ImpactRays{i + 1}",
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Color = Colors.White,
                Material = material
            };
            rays.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            _layer.AddChild(rays);
            _impactMaterials.Add(material);
        }

        Node host = NRun.Instance is { } run ? run : room.GetTree().Root;
        host.AddChild(_layer);
    }

    public static FinisherImpactPresentation Create(NCombatRoom room, int impactCount)
    {
        if (impactCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(impactCount));
        }

        return new FinisherImpactPresentation(room, impactCount);
    }

    public void SetState(IReadOnlyList<NCreature> targets, float intensity, float flash)
    {
        if (_disposed || targets.Count != _impactMaterials.Count)
        {
            return;
        }

        Vector2 viewportSize = _room.GetViewportRect().Size;
        float width = Mathf.Max(1f, viewportSize.X);
        float height = Mathf.Max(1f, viewportSize.Y);
        _backdrop.Color = new Color(0.025f, 0.002f, 0.006f, MaximumBackdropAlpha * intensity);

        for (int i = 0; i < targets.Count; i++)
        {
            Vector2 center = targets[i].Hitbox.GetGlobalRect().GetCenter();
            ShaderMaterial material = _impactMaterials[i];
            material.SetShaderParameter(ImpactUvParameter, new Vector2(center.X / width, center.Y / height));
            material.SetShaderParameter(AspectRatioParameter, width / height);
            material.SetShaderParameter(IntensityParameter, intensity);
            material.SetShaderParameter(FlashParameter, flash);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (GodotObject.IsInstanceValid(_layer))
        {
            _layer.QueueFree();
        }
    }
}
