using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Monsters;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class CombatDodgeAnimation
{
    internal const float PreImpactLeadSeconds = 0.3f;
    private const float OutwardSeconds = 0.08f;
    private const float PostImpactHoldSeconds = 0.2f;
    private const float ReturnSeconds = 0.14f;
    private const float MissingImpactTimeoutSeconds = 0.8f;
    private const float DodgeDistance = 120f;

    private static readonly Dictionary<Creature, DodgeState> ActiveDodges = [];

    public static Task Schedule(Creature creature, float fastDelay, float standardDelay)
    {
        if (creature.IsDead)
        {
            return Task.CompletedTask;
        }

        if (ActiveDodges.TryGetValue(creature, out DodgeState? active))
        {
            return active.Completion;
        }

        DodgeState state = new(creature);
        ActiveDodges[creature] = state;
        state.Completion = Run(state, fastDelay, standardDelay);
        _ = TaskHelper.RunSafely(state.Completion);
        return state.Completion;
    }

    public static Task PlayImmediate(Creature creature)
    {
        Task completion = Schedule(creature, 0f, 0f);
        NotifyImpact(creature);
        return completion;
    }

    public static void NotifyImpact(Creature creature)
    {
        if (!ActiveDodges.TryGetValue(creature, out DodgeState? state))
        {
            _ = Schedule(creature, 0f, 0f);
            state = ActiveDodges.GetValueOrDefault(creature);
        }

        state?.NotifyImpact();
    }

    private static async Task Run(DodgeState state, float fastDelay, float standardDelay)
    {
        NCreature? node = null;
        Vector2 baseline = default;
        try
        {
            await Cmd.CustomScaledWait(
                Math.Max(0f, fastDelay),
                Math.Max(0f, standardDelay));
            node = state.Creature.GetCreatureNode();
            if (node == null || !GodotObject.IsInstanceValid(node) || state.Creature.IsDead)
            {
                return;
            }

            baseline = node.Position;
            state.Node = node;
            float direction = state.Creature.Side == CombatSide.Player ? -1f : 1f;
            if (!await TweenPosition(
                state,
                baseline + Vector2.Right * DodgeDistance * direction,
                OutwardSeconds,
                Tween.EaseType.Out,
                Tween.TransitionType.Expo))
            {
                return;
            }

            Task timeout = Cmd.Wait(MissingImpactTimeoutSeconds, ignoreCombatEnd: true);
            if (await Task.WhenAny(state.FirstImpact, timeout) == state.FirstImpact)
            {
                int observedImpact;
                do
                {
                    observedImpact = state.ImpactVersion;
                    await Cmd.Wait(PostImpactHoldSeconds, ignoreCombatEnd: true);
                }
                while (observedImpact != state.ImpactVersion);
            }

            await TweenPosition(
                state,
                baseline,
                ReturnSeconds,
                Tween.EaseType.In,
                Tween.TransitionType.Cubic);
        }
        finally
        {
            state.ActiveTween?.Kill();
            if (node != null && GodotObject.IsInstanceValid(node))
            {
                node.Position = baseline;
            }

            if (ActiveDodges.TryGetValue(state.Creature, out DodgeState? active)
                && ReferenceEquals(active, state))
            {
                ActiveDodges.Remove(state.Creature);
            }
        }
    }

    private static async Task<bool> TweenPosition(
        DodgeState state,
        Vector2 target,
        float duration,
        Tween.EaseType ease,
        Tween.TransitionType transition)
    {
        NCreature? node = state.Node;
        if (node == null || !GodotObject.IsInstanceValid(node))
        {
            return false;
        }

        Tween tween = node.CreateTween();
        state.ActiveTween = tween;
        tween.TweenProperty(node, new NodePath("position"), target, duration)
            .SetEase(ease)
            .SetTrans(transition);
        if (!await TweenPlayback.AwaitCompletion(tween, node))
        {
            return false;
        }
        if (GodotObject.IsInstanceValid(node))
        {
            node.Position = target;
        }

        if (ReferenceEquals(state.ActiveTween, tween))
        {
            state.ActiveTween = null;
        }

        return true;
    }

    private sealed class DodgeState(Creature creature)
    {
        private readonly TaskCompletionSource _firstImpact =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Creature Creature { get; } = creature;
        public Task FirstImpact => _firstImpact.Task;
        public Task Completion { get; set; } = Task.CompletedTask;
        public NCreature? Node { get; set; }
        public Tween? ActiveTween { get; set; }
        public int ImpactVersion { get; private set; }

        public void NotifyImpact()
        {
            ImpactVersion++;
            _firstImpact.TrySetResult();
        }
    }
}

internal static class EnemyAttackDodgeContext
{
    private static readonly FieldInfo? AttackerAnimName =
        AccessTools.Field(typeof(AttackCommand), "_attackerAnimName");
    private static readonly FieldInfo? VisualAttacker =
        AccessTools.Field(typeof(AttackCommand), "_visualAttacker");
    private static readonly FieldInfo? WaitBeforeHit =
        AccessTools.Field(typeof(AttackCommand), "_waitBeforeHit");
    private static readonly AsyncLocal<Frame?> Current = new();

    public static Frame? Enter(AttackCommand command)
    {
        if (command.Attacker is not { IsMonster: true, Side: CombatSide.Enemy } attacker
            || command.IsRandomlyTargeted
            || !command.DamageProps.IsCardOrMonsterMove()
            || AttackerAnimName?.GetValue(command) is not string triggerName)
        {
            return null;
        }

        IReadOnlyList<Creature> targets;
        if (command.IsSingleTargeted
            && GameCompatibility.Finisher.TryReadAttackCommand(
                command,
                out GameCompatibility.AttackCommandState commandState)
            && commandState.SingleTarget != null)
        {
            targets = [commandState.SingleTarget];
        }
        else if (command.IsMultiTargeted)
        {
            targets = attacker.CombatState?.PlayerCreatures ?? [];
        }
        else
        {
            return null;
        }

        var dodgers = new HashSet<Creature>(ReferenceEqualityComparer.Instance);
        foreach (MegaCrit.Sts2.Core.Entities.Players.Player player in targets
                     .Where(target => target.IsAlive && target.Side != attacker.Side)
                     .Select(target => target.Player ?? target.PetOwner)
                     .OfType<MegaCrit.Sts2.Core.Entities.Players.Player>()
                     .Distinct())
        {
            if (player.Creature is { IsAlive: true } owner
                && owner.GetPower<EvasionPower>() is { Amount: > 0 })
            {
                dodgers.Add(owner);
            }

            Creature? yamotoKoki = player.PlayerCombatState?.Pets.FirstOrDefault(pet =>
                pet.Monster is YamotoKokiMonster && pet.IsAlive);
            if (yamotoKoki != null)
            {
                dodgers.Add(yamotoKoki);
            }
        }

        if (dodgers.Count == 0)
        {
            return null;
        }

        float[] hitWaits = WaitBeforeHit?.GetValue(command) as float[] ?? [-1f, -1f];
        Frame frame = new(
            Current.Value,
            dodgers.ToArray(),
            VisualAttacker?.GetValue(command) as Creature ?? attacker,
            triggerName,
            Math.Max(0f, hitWaits.ElementAtOrDefault(0)),
            Math.Max(0f, hitWaits.ElementAtOrDefault(1)));
        Current.Value = frame;
        return frame;
    }

    public static void RestoreCaller(Frame frame)
    {
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
        }
    }

    public static void OnAttackerAnimation(Creature creature, string triggerName, float waitTime)
    {
        Frame? frame = Current.Value;
        if (frame is not { IsActive: true }
            || creature != frame.VisualAttacker
            || !string.Equals(triggerName, frame.TriggerName, StringComparison.Ordinal))
        {
            return;
        }

        float standardUntilImpact = Math.Max(0f, waitTime) + frame.StandardHitWait;
        float fastUntilImpact = Math.Min(Math.Max(0f, waitTime) * 0.5f, 0.25f)
            + frame.FastHitWait;
        float standardDelay = Math.Max(
            0f,
            standardUntilImpact - CombatDodgeAnimation.PreImpactLeadSeconds);
        float fastDelay = Math.Max(
            0f,
            fastUntilImpact - CombatDodgeAnimation.PreImpactLeadSeconds);
        foreach (Creature dodger in frame.Dodgers)
        {
            _ = CombatDodgeAnimation.Schedule(dodger, fastDelay, standardDelay);
        }
    }

    internal sealed class Frame(
        Frame? previous,
        IReadOnlyList<Creature> dodgers,
        Creature visualAttacker,
        string triggerName,
        float fastHitWait,
        float standardHitWait)
    {
        public Frame? Previous { get; } = previous;
        public IReadOnlyList<Creature> Dodgers { get; } = dodgers;
        public Creature VisualAttacker { get; } = visualAttacker;
        public string TriggerName { get; } = triggerName;
        public float FastHitWait { get; } = fastHitWait;
        public float StandardHitWait { get; } = standardHitWait;
        public bool IsActive { get; set; } = true;
    }
}
