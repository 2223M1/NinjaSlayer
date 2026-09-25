using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Code.Nodes;

namespace NinjaSlayer.Code.ExternalAnimations;

internal sealed class FinisherActorLayerLease : IDisposable
{
    private const int MaximumCanvasZIndex = 4095;

    private readonly List<(CanvasItem Item, int Z, bool Relative)> _layers = [];
    private bool _disposed;

    private FinisherActorLayerLease(CanvasItem body)
    {
        _layers.Add((body, body.ZIndex, body.ZAsRelative));
    }

    internal static FinisherActorLayerLease AcquireBehind(NCreature actor, NCreature victim)
    {
        // Raise the victim, rather than lowering the grappler below the backdrop.
        // Include overlay/head layers and any absolute-Z descendants.
        var actorItems = new List<CanvasItem>();
        var victimItems = new List<CanvasItem>();
        void Visit(Node node, List<CanvasItem> items)
        {
            if (node is CanvasItem item) items.Add(item);
            foreach (Node child in node.GetChildren()) Visit(child, items);
        }
        Visit(actor.Visuals, actorItems);
        CanvasItem root = NinjaSlayerVisualRig.GetAirborneAnchor(victim.Visuals)
            ?? victim.Visuals.GetCurrentBody();
        Visit(root, victimItems);
        int delta = Math.Max(0, actorItems.Max(ResolveEffectiveZ) + 1 - victimItems.Min(ResolveEffectiveZ));
        var roots = victimItems.Where(item => item == root || !item.ZAsRelative)
            .Select(item => (Item: item, Z: ResolveEffectiveZ(item))).ToArray();
        var lease = new FinisherActorLayerLease(root);
        foreach (var (item, z) in roots)
        {
            if (item != root) lease._layers.Add((item, item.ZIndex, item.ZAsRelative));
            item.ZAsRelative = false;
            item.ZIndex = Math.Clamp(z + delta, -MaximumCanvasZIndex, MaximumCanvasZIndex);
        }
        return lease;
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
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var (item, z, relative) in _layers)
        {
            if (!GodotObject.IsInstanceValid(item)) continue;
            item.ZIndex = z;
            item.ZAsRelative = relative;
        }
        _layers.Clear();
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
