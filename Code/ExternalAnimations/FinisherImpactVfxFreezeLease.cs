using Godot;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
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
        foreach (NCreature target in targets.Where(IsNodeActive))
        {
            AddContainer(target.Entity.GetVfxContainer(), containers);
        }

        List<ProcessModeSnapshot> snapshots = [];
        var capturedNodes = new HashSet<ulong>();
        foreach (Node container in containers.Values)
        {
            foreach (Node vfxRoot in container.GetChildren())
            {
                if (baselineChildIds.Contains(vfxRoot.GetInstanceId())
                    || !IsNodeActive(vfxRoot)
                    || !ContainsVisualNearTargets(vfxRoot, targetRegions))
                {
                    continue;
                }

                CaptureProcessModes(vfxRoot, snapshots, capturedNodes);
            }
        }

        if (ownedVisuals != null)
            foreach (Node visual in ownedVisuals)
                CaptureProcessModes(visual, snapshots, capturedNodes);
        foreach (ProcessModeSnapshot snapshot in snapshots)
        {
            if (IsNodeActive(snapshot.Node))
            {
                snapshot.Node.ProcessMode = Node.ProcessModeEnum.Disabled;
                if (ownedVisuals != null)
                {
                    if (snapshot.Node is GpuParticles2D gpu) gpu.SpeedScale = 0;
                    if (snapshot.Node is CpuParticles2D cpu) cpu.SpeedScale = 0;
                }
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

    private static bool IsNodeActive(Node node) =>
        GodotObject.IsInstanceValid(node)
        && node.IsInsideTree()
        && !node.IsQueuedForDeletion();

    private readonly record struct ProcessModeSnapshot(Node Node, Node.ProcessModeEnum Mode, double ParticleSpeed);
}

internal static class FinisherAttackVfxBaselineContext
{
    private static readonly AsyncLocal<Frame?> Current = new();

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
        if (frame.Ranged != null) return;
        Creature? victim = FinisherAttackCommandAdapter.PredictReverseVictim(frame.Command, frame.Hits);
        if (victim?.GetCreatureNode() is not { } focus || actor.GetCreatureNode() is not { } node) return;
        frame.Approach = FinisherApproach.Create(node, focus, Godot.Vector2.One);
        bool isIai = trigger == "SlowAttack" && actor.Monster is NinjaSlayer.Monsters.DarkNinjaMonster;
        float gate = isIai
            ? SlowAttackAnimation.IaiPeakSeconds : NinjaSlayer.Code.Combat.CombatActionTimingRuntime.TriggerSeconds(waitTime);
        if (isIai) frame.Approach.ReturnDuration = SlowAttackAnimation.IaiReturnSeconds;
        frame.Approach.Start(gate + FinisherAttackCommandAdapter.AdditionalHitWait(frame.Command));
    }

    internal static void ReachImpact(Creature dealer)
    {
        if (Current.Value is { IsActive: true } frame && frame.Attacker == dealer)
            frame.Approach?.ApplyProgress(1f);
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
        try
        {
            return await task;
        }
        finally
        {
            frame.IsActive = false;
            frame.Approach?.ReleasePrediction();
            frame.Ranged?.Dispose();
        }
    }

    internal sealed class Frame(
        Frame? previous,
        AttackCommand command,
        IReadOnlySet<ulong> baselineChildIds)
    {
        public Frame? Previous { get; } = previous;
        public AttackCommand Command { get; } = command;
        public Creature Attacker => Command.Attacker!;
        internal FinisherRangedAction? Ranged { get; } =
            command.Attacker?.Monster is { } monster && FinisherRangedAction.IsRangedMove(monster)
                && FinisherRangedAction.For(command.Attacker) == null
                ? FinisherRangedAction.Begin(command.Attacker!) : null;
        public int Hits { get; set; } = 1;
        public bool Started { get; set; }
        public FinisherApproach? Approach { get; set; }
        public IReadOnlySet<ulong> BaselineChildIds { get; } = baselineChildIds;
        public bool IsActive { get; set; } = true;
    }
}
