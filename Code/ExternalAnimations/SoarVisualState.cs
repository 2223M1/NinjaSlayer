using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Nodes;

namespace NinjaSlayer.Code.ExternalAnimations;

/// <summary>
/// Tracks airborne state and drives AirborneAnchor Y offset during Soar.
/// </summary>
public static class SoarVisualState
{
    private static readonly Dictionary<Creature, float> airborneOffsets = new();

    public static void BeginAirborne(Creature creature, float riseDistance)
    {
        airborneOffsets[creature] = riseDistance;
    }

    public static bool IsAirborne(Creature creature) => airborneOffsets.ContainsKey(creature);

    public static bool TryGetAirborneOffset(Creature creature, out float riseDistance)
    {
        return airborneOffsets.TryGetValue(creature, out riseDistance);
    }

    public static void EnforceAirbornePosition(Creature creature)
    {
        if (!TryGetAirborneOffset(creature, out float riseDistance))
        {
            return;
        }

        var anchor = GetAnchor(creature);
        if (anchor == null)
        {
            return;
        }

        anchor.Position = new Vector2(anchor.Position.X, -riseDistance);
    }

    public static void EndAirborne(Creature creature)
    {
        airborneOffsets.Remove(creature);
    }

    public static void ResetVisualsToGround(Creature creature)
    {
        var anchor = GetAnchor(creature);
        if (anchor != null)
        {
            anchor.Position = Vector2.Zero;
        }

        EndAirborne(creature);
    }

    private static Node2D? GetAnchor(Creature creature)
    {
        var visuals = NCombatRoom.Instance?.GetCreatureNode(creature)?.Visuals;
        return NinjaSlayerVisualRig.GetAirborneAnchor(visuals);
    }
}
