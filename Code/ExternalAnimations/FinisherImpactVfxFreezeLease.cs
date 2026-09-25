using Godot;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.ExternalAnimations;

internal sealed class FinisherImpactVfxFreezeLease : IDisposable
{
    private readonly List<ProcessModeSnapshot> _snapshots;
    private bool _disposed;

    private FinisherImpactVfxFreezeLease(List<ProcessModeSnapshot> snapshots)
    {
        _snapshots = snapshots;
    }

    public static IReadOnlySet<ulong> CaptureBaseline(NCombatRoom room)
    {
        var containers = new Dictionary<ulong, Node>();
        AddContainer(room.CombatVfxContainer, containers);
        AddContainer(room.BackCombatVfxContainer, containers);
        foreach (NCreature creatureNode in room.CreatureNodes.Where(IsNodeActive))
        {
            AddContainer(creatureNode.Entity.GetVfxContainer(), containers);
        }

        return containers.Values
            .SelectMany(container => container.GetChildren())
            .Where(IsNodeActive)
            .Select(child => child.GetInstanceId())
            .ToHashSet();
    }

    public static FinisherImpactVfxFreezeLease Acquire(
        NCombatRoom room,
        IReadOnlyList<NCreature> targets,
        IReadOnlySet<ulong> baselineChildIds,
        float targetMargin,
        IReadOnlyList<Node>? ownedVisuals = null)
    {
        List<Rect2> targetRegions = targets
            .Where(IsNodeActive)
            .Select(target => target.Hitbox.GetGlobalRect().Grow(targetMargin))
            .ToList();
        if (targetRegions.Count == 0)
        {
            return new FinisherImpactVfxFreezeLease([]);
        }

        var containers = new Dictionary<ulong, Node>();
        AddContainer(room.CombatVfxContainer, containers);
        AddContainer(room.BackCombatVfxContainer, containers);
        foreach (NCreature target in targets.Where(IsNodeActive))
        {
            AddContainer(target.Entity.GetVfxContainer(), containers);
        }

        List<ProcessModeSnapshot> snapshots = [];
        var capturedNodes = new HashSet<ulong>();
        FinisherSession? session = FinisherSessionRegistry.GetActiveSession();
        foreach (Node container in containers.Values)
        {
            foreach (Node vfxRoot in container.GetChildren())
            {
                if (baselineChildIds.Contains(vfxRoot.GetInstanceId())
                    || !IsNodeActive(vfxRoot)
                    || session?.IsForeignVfx(vfxRoot) == true
                    || !ContainsVisualNearTargets(vfxRoot, targetRegions))
                {
                    continue;
                }

                float age = session?.ImpactVfxAge(vfxRoot) ?? 0f;
                if (vfxRoot is MegaCrit.Sts2.Core.Nodes.Vfx.NShivThrowVfx) age = Math.Max(0f, age - 0.15f);
                if (NinjaSlayer.Code.Patches.FinisherHeavyBluntSequencePatch.PrepareImpact(vfxRoot, out float impactAge))
                    age = impactAge;
                PrepareImpactParticles(vfxRoot, Math.Max(0f, 0.1f - age));
                CaptureProcessModes(vfxRoot, snapshots, capturedNodes);
            }
        }

        if (ownedVisuals != null)
            foreach (Node visual in ownedVisuals)
            {
                if (IsNodeActive(visual) && !capturedNodes.Contains(visual.GetInstanceId()))
                    PrepareImpactParticles(visual, 0.1f);
                CaptureProcessModes(visual, snapshots, capturedNodes);
            }
        foreach (ProcessModeSnapshot snapshot in snapshots)
        {
            if (IsNodeActive(snapshot.Node))
            {
                snapshot.Node.ProcessMode = Node.ProcessModeEnum.Disabled;
                if (snapshot.Node is GpuParticles2D gpu) gpu.SpeedScale = 0;
                if (snapshot.Node is CpuParticles2D cpu) cpu.SpeedScale = 0;
            }
        }

        return new FinisherImpactVfxFreezeLease(snapshots);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (ProcessModeSnapshot snapshot in _snapshots)
        {
            if (IsNodeActive(snapshot.Node))
            {
                snapshot.Node.ProcessMode = snapshot.Mode;
                if (snapshot.Node is GpuParticles2D gpu) gpu.SpeedScale = snapshot.ParticleSpeed;
                if (snapshot.Node is CpuParticles2D cpu) cpu.SpeedScale = (float)snapshot.ParticleSpeed;
            }
        }

        _snapshots.Clear();
    }

    private static void AddContainer(Node? container, IDictionary<ulong, Node> containers)
    {
        if (container != null && IsNodeActive(container))
        {
            containers.TryAdd(container.GetInstanceId(), container);
        }
    }

    private static bool ContainsVisualNearTargets(Node node, IReadOnlyList<Rect2> targetRegions)
    {
        Vector2? position = node switch
        {
            Control control => control.GetGlobalRect().GetCenter(),
            Node2D node2D => node2D.GlobalPosition,
            _ => null
        };
        if (position.HasValue && targetRegions.Any(region => region.HasPoint(position.Value)))
        {
            return true;
        }

        return node.GetChildren().Any(child =>
            IsNodeActive(child) && ContainsVisualNearTargets(child, targetRegions));
    }

    private static void CaptureProcessModes(
        Node node,
        ICollection<ProcessModeSnapshot> snapshots,
        ISet<ulong> capturedNodes)
    {
        if (!IsNodeActive(node) || !capturedNodes.Add(node.GetInstanceId()))
        {
            return;
        }

        snapshots.Add(new ProcessModeSnapshot(node, node.ProcessMode, node switch { GpuParticles2D gpu => gpu.SpeedScale, CpuParticles2D cpu => cpu.SpeedScale, _ => 1f }));
        foreach (Node child in node.GetChildren())
        {
            CaptureProcessModes(child, snapshots, capturedNodes);
        }
    }

    private static void PrepareImpactParticles(Node node, float seconds)
    {
        if (seconds > 0f)
        {
            if (node is GpuParticles2D { Emitting: true, ProcessMaterial: not null } gpu)
                gpu.RequestParticlesProcess(seconds);
            else if (node is CpuParticles2D { Emitting: true } cpu)
                cpu.RequestParticlesProcess(seconds);
            else if (node is AnimationPlayer player && player.IsPlaying())
                player.Seek(player.CurrentAnimationPosition + seconds, update: true, updateOnly: true);
        }
        foreach (Node child in node.GetChildren())
        {
            // Flight particles retain their actual arrival state, including the ordinary shuriken head.
            if (node is MegaCrit.Sts2.Core.Nodes.Vfx.NShivThrowVfx && child.Name == "throw_container") continue;
            PrepareImpactParticles(child, seconds);
        }
    }

    private static bool IsNodeActive(Node node) =>
        GodotObject.IsInstanceValid(node)
        && node.IsInsideTree()
        && !node.IsQueuedForDeletion();

    private readonly record struct ProcessModeSnapshot(Node Node, Node.ProcessModeEnum Mode, double ParticleSpeed);
}

internal static class FinisherAttackVfxBaselineContext
{
    private static readonly AsyncLocal<Frame?> Current = new();
    internal static Creature? CurrentAttacker => Current.Value is { IsActive: true } frame ? frame.Attacker : null;

    internal static Frame? For(Creature? actor) =>
        Current.Value is { IsActive: true } frame && frame.Attacker == actor ? frame : null;

    internal static void ObserveDamage(Creature? actor, CardModel? card, ValueProp props)
    {
        if (card == null && For(actor) is { } frame && props == frame.Command.DamageProps)
            frame.DamageWaves++;
    }

    public static Frame? Enter(AttackCommand command)
    {
        if (command.Attacker is not { IsMonster: true } attacker
            || attacker.CombatState is not { } combatState
            || combatState.Players.All(player => player.Character is not INinjaSlayerCharacter)
            || NCombatRoom.Instance is not { } room
            || !GodotObject.IsInstanceValid(room.CombatVfxContainer))
        {
            return null;
        }

        var frame = new Frame(
            Current.Value,
            command,
            FinisherImpactVfxFreezeLease.CaptureBaseline(room));
        Current.Value = frame;
        return frame;
    }

    public static IReadOnlySet<ulong>? GetBaseline(Creature dealer)
    {
        for (Frame? frame = Current.Value; frame != null; frame = frame.Previous)
        {
            if (frame.IsActive && frame.Attacker == dealer)
            {
                return frame.BaselineChildIds;
            }
        }

        return null;
    }

    internal static void ObserveHitCount(AttackCommand command, decimal count)
    {
        if (Current.Value is { } frame && ReferenceEquals(frame.Command, command))
            frame.Hits = (int)Math.Ceiling(Math.Max(0m, count));
    }

    internal static void BeginApproach(Creature actor, string trigger, float waitTime)
    {
        if (trigger is "Hit" or "BlockedHit" or "Dodge" or "Dead"
            || Current.Value is not { IsActive: true, Started: false } frame || frame.Attacker != actor) return;
        frame.Started = true;
        Creature? victim = FinisherAttackCommandAdapter.PredictReverseVictim(frame.Command, frame.Hits, out var targets);
        if (victim?.GetCreatureNode() is not { } focus || actor.GetCreatureNode() is not { } node) return;
        if (frame.Ranged == null)
        {
            frame.Approach = FinisherApproach.Create(node, focus, FinisherTimeline.MeleeSquash(FinisherTimeline.PreviewProfile));
            bool isIai = trigger == "SlowAttack" && actor.Monster is NinjaSlayer.Monsters.DarkNinjaMonster;
            float gate = isIai
                ? SlowAttackAnimation.IaiPeakSeconds : NinjaSlayer.Code.Combat.CombatActionTimingRuntime.TriggerSeconds(waitTime);
            if (isIai) frame.Approach.ReturnDuration = SlowAttackAnimation.IaiReturnSeconds;
            frame.Approach.Start(gate + FinisherAttackCommandAdapter.AdditionalHitWait(frame.Command));
        }
        NinjaSlayerDeathClassifier.TryStartPredictedReverseFinisher(frame, victim, targets);
    }

    internal static void ReachImpact(Creature dealer)
    {
        if (Current.Value is { IsActive: true } frame && frame.Attacker == dealer)
            FinisherApproach.ReachImpact(dealer);
    }

    public static void RestoreCaller(Frame frame)
    {
        frame.Ranged?.RestoreCaller();
        if (ReferenceEquals(Current.Value, frame))
        {
            Current.Value = frame.Previous;
        }
    }

    public static async Task<AttackCommand> Complete(Task<AttackCommand> task, Frame frame)
    {
        bool succeeded = false;
        try
        {
            AttackCommand result = await task;
            succeeded = true;
            return result;
        }
        finally
        {
            await frame.Complete(succeeded);
        }
    }

    internal sealed class Frame(
        Frame? previous,
        AttackCommand command,
        IReadOnlySet<ulong> baselineChildIds) : IAsyncDisposable
    {
        private bool _completed;
        public Frame? Previous { get; } = previous;
        public AttackCommand Command { get; } = command;
        public Creature Attacker => Command.Attacker!;
        internal FinisherRangedAction? Ranged { get; } =
            command.Attacker?.Monster is { } monster && FinisherRangedAction.IsRangedMove(monster)
                && FinisherRangedAction.For(command.Attacker) == null
                ? FinisherRangedAction.Begin(command.Attacker!) : null;
        public int Hits { get; set; } = 1;
        public int DamageWaves { get; set; }
        public FinisherSession? ReverseSession { get; set; }
        public bool Started { get; set; }
        public FinisherApproach? Approach { get; set; }
        public IReadOnlySet<ulong> BaselineChildIds { get; } = baselineChildIds;
        public bool IsActive { get; set; } = true;

        internal async Task Complete(bool playPose)
        {
            if (_completed) return;
            _completed = true;
            try
            {
                if (ReverseSession is { } session) await session.CompleteAsync(playPose);
            }
            finally
            {
                IsActive = false;
                Approach?.ReleasePrediction();
                Ranged?.Dispose();
            }
        }

        public ValueTask DisposeAsync()
        {
            RestoreCaller(this);
            return new ValueTask(Complete(playPose: false));
        }
    }
}
