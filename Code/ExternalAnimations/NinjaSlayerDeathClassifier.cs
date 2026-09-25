using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Content;
using NinjaSlayer.Scripts;

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

        FinisherAttackVfxBaselineContext.ReachImpact(dealer);

        var previousCaptures = new Dictionary<Creature, IncomingDamageCapture?>();
        IReadOnlySet<ulong> baseline = FinisherRangedAction.For(dealer)?.Baseline
            ?? FinisherAttackVfxBaselineContext.GetBaseline(dealer)
            ?? FinisherImpactVfxFreezeLease.CaptureBaseline(room);
        var capture = new IncomingDamageCapture(
            dealer,
            baseline,
            ninjaSlayerTargets,
            previousCaptures,
            FinisherAttackVfxBaselineContext.For(dealer));
        foreach (Creature target in ninjaSlayerTargets)
        {
            previousCaptures[target] = IncomingCaptures.GetValueOrDefault(target);
            IncomingCaptures[target] = capture;
        }

        return capture;
    }

    public static bool TryStartReverseFinisher(Creature target, decimal amount)
    {
        if (target.Player?.Character is not INinjaSlayerCharacter
            || target.CurrentHp <= 0
            || amount < target.CurrentHp
            || !IncomingCaptures.TryGetValue(target, out IncomingDamageCapture? capture)
            || capture.IsCompleted
            || capture.Session != null)
        {
            return false;
        }

        if (!TryCreateReverseFinisher(target, capture.Dealer, capture.Targets,
                capture.VfxBaselineChildIds, capture.AttackFrame, out FinisherSession? session)) return false;
        capture.Session = session;
        return true;
    }

    internal static void TryStartPredictedReverseFinisher(
        FinisherAttackVfxBaselineContext.Frame frame, Creature target, IReadOnlyList<Creature> targets)
    {
        if (frame.Hits <= 1 || frame.ReverseSession != null
            || FinisherTimeline.PreviewProfile == FinisherPreviewProfile.A) return;
        TryCreateReverseFinisher(target, frame.Attacker, targets, frame.BaselineChildIds, frame, out _);
    }

    private static bool TryCreateReverseFinisher(
        Creature target, Creature dealer, IReadOnlyList<Creature> targets, IReadOnlySet<ulong> baseline,
        FinisherAttackVfxBaselineContext.Frame? frame, out FinisherSession? session)
    {
        session = null;
        if (target.Player?.Character is not INinjaSlayerCharacter
            || target.CombatState is not { } combatState
            || !IsValidEnemyDealer(target, dealer)
            || !FinisherProtectionService.CanProtectLethalDamage(out _)
            || !Hook.ShouldDie(target.Player.RunState, combatState, target, out _)
            || NCombatRoom.Instance is not { } room)
        {
            return false;
        }

        if (FinisherSessionRegistry.HasRegisteredSessionForCombat(combatState, room))
        {
            return false;
        }

        NCreature? dealerNode = room.GetCreatureNode(dealer);
        NCreature? focusNode = room.GetCreatureNode(target);
        List<Creature> victims = targets
            .Where(candidate => candidate.IsAlive && candidate.Player?.Character is INinjaSlayerCharacter
                && ReferenceEquals(candidate.CombatState, combatState)
                && room.GetCreatureNode(candidate) != null)
            .Distinct()
            .ToList();
        if (dealerNode == null
            || focusNode == null
            || victims.Count == 0
            || !CombatCinematicCameraLease.TryAcquire(
                room,
                "NinjaSlayer reverse finisher",
                out CombatCinematicCameraLease? camera))
        {
            return false;
        }

        if (!FinisherSessionRegistry.TryRegisterSession(
                new FinisherSessionRequest(
                    FinisherScenarioKind.EnemyExecutesNinjaSlayer,
                    FinisherCompletionCondition.AnyCandidateLethal,
                    dealer,
                    dealerNode,
                    focusNode,
                    victims,
                    camera,
                    CardPlay: null,
                    RequiresAfterCardPlayed: false,
                    ResolvedHits: frame?.Hits ?? 1,
                    VfxBaselineChildIds: baseline,
                    RangedAction: FinisherRangedAction.For(dealer),
                    ObservedPrimaryHits: frame?.DamageWaves ?? 1),
                combatState,
                room,
                out session))
        {
            camera.Dispose();
            return false;
        }

        try
        {
            session.Begin();
            if (frame != null) frame.ReverseSession = session;
            Entry.Logger.Info(
                $"Reverse finisher session {session.SessionId} started: dealer={dealer}, victims={victims.Count}.");
            return true;
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"Could not begin reverse finisher session {session.SessionId}: {ex}");
            _ = CancelFailedSession(session);
            return false;
        }
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
            IEnumerable<DamageResult> results;
            try
            {
                results = await damageTask;
            }
            catch (Exception originalFailure)
            {
                if (capture.AttackFrame == null && capture.Session is { } failedSession)
                {
                    try
                    {
                        await failedSession.CompleteAsync(playPose: false);
                    }
                    catch (Exception completionFailure)
                    {
                        throw new AggregateException(
                            "Incoming damage and reverse finisher cleanup both failed.",
                            originalFailure,
                            completionFailure);
                    }
                }

                throw;
            }

            if (capture.AttackFrame == null && capture.Session is { } session)
            {
                await session.CompleteAsync(playPose: true);
            }

            return results;
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

    private static async Task CancelFailedSession(FinisherSession session)
    {
        try
        {
            await session.CancelAsync();
        }
        catch (Exception ex)
        {
            Entry.Logger.Error(
                $"Reverse finisher session {session.SessionId} cleanup after begin failure failed: {ex}");
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
        IReadOnlyDictionary<Creature, IncomingDamageCapture?> previousCaptures,
        FinisherAttackVfxBaselineContext.Frame? attackFrame)
    {
        public Creature Dealer { get; } = dealer;
        public IReadOnlySet<ulong> VfxBaselineChildIds { get; } = vfxBaselineChildIds;
        public IReadOnlyList<Creature> Targets { get; } = targets;
        public IReadOnlyDictionary<Creature, IncomingDamageCapture?> PreviousCaptures { get; } = previousCaptures;
        public FinisherSession? Session { get; set; }
        public FinisherAttackVfxBaselineContext.Frame? AttackFrame { get; } = attackFrame;
        public bool IsCompleted { get; set; }
    }

    private sealed class ConsumedFatalDamage
    {
        public DamageReceivedEntry? Entry { get; set; }
    }
}
