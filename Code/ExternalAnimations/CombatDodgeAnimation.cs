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
    private static readonly FieldInfo AttackerAnimName =
        AccessTools.Field(typeof(AttackCommand), "_attackerAnimName")
        ?? throw new MissingFieldException(typeof(AttackCommand).FullName, "_attackerAnimName");
    private static readonly FieldInfo VisualAttacker =
        AccessTools.Field(typeof(AttackCommand), "_visualAttacker")
        ?? throw new MissingFieldException(typeof(AttackCommand).FullName, "_visualAttacker");
    private static readonly FieldInfo WaitBeforeHit =
        AccessTools.Field(typeof(AttackCommand), "_waitBeforeHit")
        ?? throw new MissingFieldException(typeof(AttackCommand).FullName, "_waitBeforeHit");
    private static readonly FieldInfo SingleTarget =
        AccessTools.Field(typeof(AttackCommand), "_singleTarget")
        ?? throw new MissingFieldException(typeof(AttackCommand).FullName, "_singleTarget");
    private static readonly AsyncLocal<Frame?> Current = new();

    public static Frame? Enter(AttackCommand command)
    {
        if (command.Attacker is not { IsMonster: true, Side: CombatSide.Enemy } attacker
            || command.IsRandomlyTargeted
            || !command.DamageProps.IsCardOrMonsterMove())
        {
            return null;
        }

        EnemyAttackPresentation presentation = ReadPresentation(command, attacker);
        IReadOnlyList<Creature> targets;
        if (command.IsSingleTargeted && ReadSingleTarget(command) is { } singleTarget)
        {
            targets = [singleTarget];
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

            if (attacker.CombatState is not { } combatState)
            {
                continue;
            }

            foreach (Creature companion in combatState.Creatures.Where(creature =>
                         creature.PetOwner == player
                         && creature.IsAlive
                         && creature.Monster is YamotoKokiMonster or SawatariMonster or YukanoMonster))
            {
                dodgers.Add(companion);
            }
        }

        if (dodgers.Count == 0)
        {
            return null;
        }

        Frame frame = new(
            Current.Value,
            dodgers.ToArray(),
            presentation.VisualAttacker,
            presentation.TriggerName,
            presentation.FastHitWait,
            presentation.StandardHitWait);
        Current.Value = frame;
        return frame;
    }

    private static EnemyAttackPresentation ReadPresentation(
        AttackCommand command,
        Creature fallbackAttacker)
    {
        string triggerName = AttackerAnimName.GetValue(command) as string
            ?? throw new InvalidOperationException(
                "AttackCommand._attackerAnimName is not an initialized string.");
        Creature visualAttacker = VisualAttacker.GetValue(command) switch
        {
            null => fallbackAttacker,
            Creature creature => creature,
            _ => throw new InvalidOperationException(
                "AttackCommand._visualAttacker has an unexpected runtime type.")
        };
        float[] hitWaits = WaitBeforeHit.GetValue(command) as float[]
            ?? throw new InvalidOperationException(
                "AttackCommand._waitBeforeHit has an unexpected runtime type.");
        if (hitWaits.Length < 2)
        {
            throw new InvalidOperationException(
                "AttackCommand._waitBeforeHit does not contain both hit timings.");
        }

        return new EnemyAttackPresentation(
            triggerName,
            visualAttacker,
            Math.Max(0f, hitWaits[0]),
            Math.Max(0f, hitWaits[1]));
    }

    private static Creature? ReadSingleTarget(AttackCommand command) =>
        SingleTarget.GetValue(command) switch
        {
            null => null,
            Creature target => target,
            _ => throw new InvalidOperationException(
                "AttackCommand._singleTarget has an unexpected runtime type.")
        };

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

    private readonly record struct EnemyAttackPresentation(
        string TriggerName,
        Creature VisualAttacker,
        float FastHitWait,
        float StandardHitWait);
}
