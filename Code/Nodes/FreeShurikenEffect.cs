using Godot;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace NinjaSlayer.Code.Nodes;

internal sealed partial class FreeShurikenEffect : Node2D
{
    private GpuParticles2D[] _flight = [], _impact = [];
    private float _age;
    private bool _hit;

    internal static FreeShurikenEffect TakeParticles(NShivThrowVfx authored, float distance)
    {
        var effect = new FreeShurikenEffect { Name = "FreeShuriken", Transform = authored.Transform };
        effect._flight = authored.Get("_throwParticles").AsGodotArray<GpuParticles2D>().ToArray();
        effect._impact = authored.Get("_impactParticles").AsGodotArray<GpuParticles2D>().ToArray();
        var head = authored.GetNode<GpuParticles2D>("throw_container/ShurikenHead");
        effect._flight = [.. effect._flight, head];
        float originalSpeed = ((ParticleProcessMaterial)head.ProcessMaterial).InitialVelocityMin;
        float speedRatio = distance / (.15f * originalSpeed);
        foreach (GpuParticles2D particle in effect._flight)
        {
            var material = (ParticleProcessMaterial)particle.ProcessMaterial.Duplicate();
            material.InitialVelocityMin *= speedRatio;
            material.InitialVelocityMax *= speedRatio;
            particle.ProcessMaterial = material;
        }
        foreach (Node descendant in authored.FindChildren("*", "", true, false)) descendant.Owner = null;
        foreach (Node child in authored.GetChildren())
        {
            authored.RemoveChild(child);
            effect.AddChild(child);
        }
        // Reuse the authored particles, but their sequence is owned by physical contact.
        authored.Free();
        return effect;
    }

    public override void _Ready()
    {
        foreach (GpuParticles2D particle in _impact) particle.Emitting = false;
        foreach (GpuParticles2D particle in _flight) particle.Restart();
    }

    public override void _Process(double delta)
    {
        if (Engine.TimeScale <= 0d) return;
        _age += (float)(delta / Engine.TimeScale);
        foreach (GpuParticles2D particle in _flight.Concat(_impact)) particle.SpeedScale = 1d / Engine.TimeScale;
        if (_age > 2.2f) QueueFree();
    }

    internal void Hit(Vector2 globalPoint)
    {
        if (_hit) return;
        _hit = true;
        Vector2 shift = globalPoint - GlobalPosition;
        foreach (GpuParticles2D particle in _impact)
        {
            particle.GlobalPosition += shift;
            particle.Restart();
        }
        foreach (GpuParticles2D particle in _flight) particle.Emitting = false;
    }
}
