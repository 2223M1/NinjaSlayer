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
        bool isEnemyKill = dealer != null
            && dealer != creature
            && dealer.Side != creature.Side
            && NCombatRoom.Instance?.GetCreatureNode(dealer) != null;
        IReadOnlySet<ulong> baseline = isEnemyKill
            && IncomingCaptures.TryGetValue(creature, out IncomingDamageCapture? capture)
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

        var capture = new IncomingDamageCapture(
            dealer,
            room.CombatVfxContainer.GetChildren()
                .Select(child => child.GetInstanceId())
                .ToHashSet(),
            ninjaSlayerTargets);
        foreach (Creature target in ninjaSlayerTargets)
        {
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
            foreach (Creature target in capture.Targets)
            {
                if (IncomingCaptures.TryGetValue(target, out IncomingDamageCapture? active)
                    && ReferenceEquals(active, capture))
                {
                    IncomingCaptures.Remove(target);
                }
            }
        }
    }

    private static DamageReceivedEntry? FindFatalEntry(Creature creature) =>
        CombatManager.Instance?.History.Entries
            .OfType<DamageReceivedEntry>()
            .LastOrDefault(entry => entry.Receiver == creature && entry.Result.WasTargetKilled);

    private sealed record IncomingDamageCapture(
        Creature Dealer,
        IReadOnlySet<ulong> VfxBaselineChildIds,
        IReadOnlyList<Creature> Targets);

    private sealed class ConsumedFatalDamage
    {
        public DamageReceivedEntry? Entry { get; set; }
    }
}
