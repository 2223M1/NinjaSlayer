using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace NinjaSlayer.Code.ExternalAnimations;

internal sealed class FinisherActorLayerLease : IDisposable
{
    private const int MaximumCanvasZIndex = 4095;

    private readonly CanvasItem _body;
    private readonly int _originalZIndex;
    private readonly bool _originalZAsRelative;
    private int _disposed;

    private FinisherActorLayerLease(CanvasItem body)
    {
        _body = body;
        _originalZIndex = body.ZIndex;
        _originalZAsRelative = body.ZAsRelative;
    }

    public static FinisherActorLayerLease? TryAcquire(
        NCreature actor,
        IEnumerable<NCreature> victims)
    {
        Node2D body = actor.Visuals.GetCurrentBody();
        if (!GodotObject.IsInstanceValid(body))
        {
            return null;
        }

        var lease = new FinisherActorLayerLease(body);
        int highestVictimZ = int.MinValue;
        foreach (NCreature victim in victims.Where(GodotObject.IsInstanceValid))
        {
            Node2D victimBody = victim.Visuals.GetCurrentBody();
            if (GodotObject.IsInstanceValid(victimBody))
            {
                highestVictimZ = Math.Max(highestVictimZ, ResolveEffectiveZ(victimBody));
            }

            CanvasItem? healthBar = victim.GetNodeOrNull<CanvasItem>("%HealthBar")
                ?? victim.Visuals.GetNodeOrNull<CanvasItem>("%HealthBar");
            if (healthBar != null && GodotObject.IsInstanceValid(healthBar))
            {
                highestVictimZ = Math.Max(highestVictimZ, ResolveEffectiveZ(healthBar));
            }
        }

        body.ZAsRelative = false;
        body.ZIndex = Math.Clamp(
            highestVictimZ == int.MinValue ? ResolveEffectiveZ(body) + 1 : highestVictimZ + 1,
            -MaximumCanvasZIndex,
            MaximumCanvasZIndex);
        return lease;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0
            || !GodotObject.IsInstanceValid(_body))
        {
            return;
        }

        _body.ZIndex = _originalZIndex;
        _body.ZAsRelative = _originalZAsRelative;
    }

    private static int ResolveEffectiveZ(CanvasItem item)
    {
        int z = item.ZIndex;
        CanvasItem current = item;
        while (current.ZAsRelative && current.GetParent() is CanvasItem parent)
        {
            z = Math.Clamp(z + parent.ZIndex, -MaximumCanvasZIndex, MaximumCanvasZIndex);
            current = parent;
        }

        return z;
    }
}
