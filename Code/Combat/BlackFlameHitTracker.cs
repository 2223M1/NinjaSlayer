using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace NinjaSlayer.Code.Combat;

internal static class BlackFlameHitTracker
{
    private static readonly ConditionalWeakTable<CardPlay, HitSet> HitsByPlay = new();

    public static void Record(CardPlay cardPlay, IEnumerable<DamageResult> results)
    {
        HitSet hitSet = HitsByPlay.GetOrCreateValue(cardPlay);
        lock (hitSet.SyncRoot)
        {
            foreach (DamageResult result in results)
            {
                if (result.TotalDamage > 0)
                {
                    hitSet.Targets.Add(result.Receiver);
                }
            }
        }
    }

    public static IReadOnlyList<Creature> TakeLiveOpponents(CardPlay cardPlay)
    {
        if (!HitsByPlay.TryGetValue(cardPlay, out HitSet? hitSet))
        {
            return [];
        }

        HitsByPlay.Remove(cardPlay);
        lock (hitSet.SyncRoot)
        {
            return hitSet.Targets
                .Where(target => !target.IsDead && target.Side != cardPlay.Player.Creature.Side)
                .ToList();
        }
    }

    private sealed class HitSet
    {
        public object SyncRoot { get; } = new();
        public HashSet<Creature> Targets { get; } = new(ReferenceEqualityComparer.Instance);
    }
}
