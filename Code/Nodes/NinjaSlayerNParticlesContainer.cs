using System.Reflection;
using Godot;
using Godot.Collections;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;

namespace NinjaSlayer.Code.Nodes;

[GlobalClass]
public partial class NinjaSlayerNParticlesContainer : NParticlesContainer
{
    private static readonly FieldInfo? ParticlesField = typeof(NParticlesContainer)
        .GetField("_particles", BindingFlags.Instance | BindingFlags.NonPublic);

    [Export(PropertyHint.None, "")]
    private Array<GpuParticles2D>? _particles;

    public override void _Ready()
    {
        base._Ready();

        if (_particles is not { Count: > 0 })
        {
            _particles = [];
            foreach (var child in GetChildren())
            {
                if (child is GpuParticles2D particles)
                {
                    _particles.Add(particles);
                }
            }
        }

        ParticlesField?.SetValue(this, _particles);
    }
}
