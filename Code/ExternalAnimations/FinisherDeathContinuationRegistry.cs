using MegaCrit.Sts2.Core.Entities.Creatures;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class FinisherDeathContinuationRegistry
{
    private static readonly object Sync = new();
    private static readonly Dictionary<Creature, long> ReverseFlights = new(ReferenceEqualityComparer.Instance);

    public static void Arm(IEnumerable<Creature> creatures, long sessionId)
    {
        lock (Sync)
        {
            foreach (Creature creature in creatures)
            {
                ReverseFlights[creature] = sessionId;
            }
        }
    }

    public static bool TryConsumeReverseFlight(Creature creature)
    {
        lock (Sync)
        {
            return ReverseFlights.Remove(creature);
        }
    }

    public static void Clear(long sessionId)
    {
        lock (Sync)
        {
            foreach (Creature creature in ReverseFlights
                         .Where(pair => pair.Value == sessionId)
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                ReverseFlights.Remove(creature);
            }
        }
    }
}
