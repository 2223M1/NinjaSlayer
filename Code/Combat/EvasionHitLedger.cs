using MegaCrit.Sts2.Core.Entities.Creatures;

namespace NinjaSlayer.Code.Combat;

internal sealed class EvasionHitLedger
{
    private readonly Dictionary<Creature, TargetHits> _hitsByAttacker =
        new(ReferenceEqualityComparer.Instance);

    public void RecordEvaded(Creature attacker, Creature target) =>
        GetHits(attacker).Evaded.Add(target);

    public void RecordConnected(Creature attacker, Creature target) =>
        GetHits(attacker).Connected.Add(target);

    public bool WasOnlyEvaded(Creature attacker, Creature target) =>
        _hitsByAttacker.TryGetValue(attacker, out TargetHits? hits)
        && hits.Evaded.Contains(target)
        && !hits.Connected.Contains(target);

    private TargetHits GetHits(Creature attacker)
    {
        if (!_hitsByAttacker.TryGetValue(attacker, out TargetHits? hits))
        {
            hits = new TargetHits();
            _hitsByAttacker.Add(attacker, hits);
        }

        return hits;
    }

    private sealed class TargetHits
    {
        public HashSet<Creature> Evaded { get; } = new(ReferenceEqualityComparer.Instance);
        public HashSet<Creature> Connected { get; } = new(ReferenceEqualityComparer.Instance);
    }
}
