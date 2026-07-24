using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class ArchitectVictoryCleanup
{
    private static readonly ConditionalWeakTable<Creature, Marker> Pending = new();

    public static void Mark(Creature creature)
    {
        Pending.Remove(creature);
        Pending.Add(creature, new Marker());
    }

    public static bool TryConsume(Creature creature) => Pending.Remove(creature);

    public static void Clear(Creature creature) => Pending.Remove(creature);

    private sealed class Marker;
}
