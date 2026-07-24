using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class NinjaSlayerDeathClassifier
{
    private static readonly ConditionalWeakTable<Creature, ConsumedFatalDamage> ConsumedEntries = new();
    private static readonly Dictionary<Creature, IncomingDamageCapture> IncomingCaptures = [];

    public static NinjaSlayerDeathContext CreateContext(Creature creature)
    {
        DamageReceivedEntry? fatalEntry = FindFatalEntry(creature);
        var consumed = ConsumedEntries.GetOrCreateValue(creature);
        IncomingCaptures.TryGetValue(creature, out IncomingDamageCapture? capture);
        if (capture != null && IsValidEnemyDealer(creature, capture.Dealer))
        {
            if (fatalEntry != null)
            {
                consumed.Entry = fatalEntry;
            }

            return new NinjaSlayerDeathContext(
                NinjaSlayerDeathKind.EnemyKill,
                fatalEntry,
                capture.Dealer,
                capture.VfxBaselineChildIds);
        }

        if (fatalEntry == null || ReferenceEquals(consumed.Entry, fatalEntry))
        {
            return new NinjaSlayerDeathContext(
                NinjaSlayerDeathKind.Other,
                null,
                null,
                new HashSet<ulong>());
        }

        consumed.Entry = fatalEntry;
        Creature? dealer = fatalEntry.Dealer;
        bool isEnemyKill = IsValidEnemyDealer(creature, dealer);
        IReadOnlySet<ulong> baseline = isEnemyKill
            && capture != null
            && capture.Dealer == dealer
                ? capture.VfxBaselineChildIds
                : new HashSet<ulong>();
        return new NinjaSlayerDeathContext(
            isEnemyKill ? NinjaSlayerDeathKind.EnemyKill : NinjaSlayerDeathKind.Other,
            fatalEntry,
            isEnemyKill ? dealer : null,
            baseline);
    }

    public static void MarkCurrentFatalDamageConsumed(Creature creature)
    {
        if (FindFatalEntry(creature) is { } fatalEntry)
        {
            ConsumedEntries.GetOrCreateValue(creature).Entry = fatalEntry;
        }
    }

    public static object? BeginIncomingDamageCapture(IEnumerable<Creature>? targets, Creature? dealer)
    {
        NCombatRoom? room = NCombatRoom.Instance;
        if (dealer == null || room == null || targets == null)
        {
            return null;
        }

        List<Creature> ninjaSlayerTargets = targets
            .Where(target => target.Player?.Character is INinjaSlayerCharacter
                && target != dealer
                && target.Side != dealer.Side)
            .Distinct()
            .ToList();
        if (ninjaSlayerTargets.Count == 0)
        {
            return null;
        }

        var previousCaptures = new Dictionary<Creature, IncomingDamageCapture?>();
        var capture = new IncomingDamageCapture(
            dealer,
            room.CombatVfxContainer.GetChildren()
                .Select(child => child.GetInstanceId())
                .ToHashSet(),
            ninjaSlayerTargets,
            previousCaptures);
        foreach (Creature target in ninjaSlayerTargets)
        {
            previousCaptures[target] = IncomingCaptures.GetValueOrDefault(target);
            IncomingCaptures[target] = capture;
        }

        return capture;
    }

    public static async Task<IEnumerable<DamageResult>> CompleteIncomingDamageCapture(
        Task<IEnumerable<DamageResult>> damageTask,
        object? state)
    {
        if (state is not IncomingDamageCapture capture)
        {
            return await damageTask;
        }

        try
        {
            return await damageTask;
        }
        finally
        {
            capture.IsCompleted = true;
            foreach (Creature target in capture.Targets)
            {
                if (IncomingCaptures.TryGetValue(target, out IncomingDamageCapture? active)
                    && ReferenceEquals(active, capture))
                {
                    IncomingDamageCapture? previous = capture.PreviousCaptures.GetValueOrDefault(target);
                    if (previous is { IsCompleted: false })
                    {
                        IncomingCaptures[target] = previous;
                    }
                    else
                    {
                        IncomingCaptures.Remove(target);
                    }
                }
            }
        }
    }

    private static bool IsValidEnemyDealer(Creature creature, Creature? dealer) =>
        dealer != null
        && dealer != creature
        && dealer.Side != creature.Side
        && NCombatRoom.Instance?.GetCreatureNode(dealer) != null;

    private static DamageReceivedEntry? FindFatalEntry(Creature creature) =>
        CombatManager.Instance?.History.Entries
            .OfType<DamageReceivedEntry>()
            .LastOrDefault(entry => entry.Receiver == creature && entry.Result.WasTargetKilled);

    private sealed class IncomingDamageCapture(
        Creature dealer,
        IReadOnlySet<ulong> vfxBaselineChildIds,
        IReadOnlyList<Creature> targets,
        IReadOnlyDictionary<Creature, IncomingDamageCapture?> previousCaptures)
    {
        public Creature Dealer { get; } = dealer;
        public IReadOnlySet<ulong> VfxBaselineChildIds { get; } = vfxBaselineChildIds;
        public IReadOnlyList<Creature> Targets { get; } = targets;
        public IReadOnlyDictionary<Creature, IncomingDamageCapture?> PreviousCaptures { get; } = previousCaptures;
        public bool IsCompleted { get; set; }
    }

    private sealed class ConsumedFatalDamage
    {
        public DamageReceivedEntry? Entry { get; set; }
    }
}
