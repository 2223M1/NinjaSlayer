using System.Reflection;
using Godot;
using Godot.Collections;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;

namespace NinjaSlayer.Code.Nodes;

public partial class NinjaSlayerNParticlesContainer : NParticlesContainer
{
    private static readonly FieldInfo ParticlesField = typeof(NParticlesContainer)
        .GetField("_particles", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(NParticlesContainer).FullName, "_particles");

    public override void _Ready()
    {
        if (ParticlesField.GetValue(this) is not Array<GpuParticles2D> { Count: > 0 })
        {
            var particles = new Array<GpuParticles2D>();
            foreach (var child in GetChildren())
            {
                if (child is GpuParticles2D particle)
                {
                    particles.Add(particle);
                }
            }

            ParticlesField.SetValue(this, particles);
        }

        base._Ready();
    }
}
