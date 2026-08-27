using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Lifecycle;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class NinjaSlayerRapidAnimationCoordinator
{
    private static readonly Dictionary<Creature, ActionState> States = [];
    private static readonly Dictionary<Creature, VisualTailState> VisualTails = [];
    private static readonly HashSet<Creature> Participants = [];
    private static NCombatRoom? _subscribedRoom;
    private static CombatManager? _subscribedCombatManager;
    private static long _visualTailGeneration;

    public static void EnsureLifecycle(Creature creature)
    {
        EnsureRoomSubscription();
        Participants.Add(creature);
    }

    public static async Task PlayAttackToPeak(
        Creature creature,
        float distance,
        float firstPeakSeconds,
        Func<float, float> outboundCurve,
        bool reverseDirection = false,
        float returnSeconds = RapidAttackTrajectory.ReturnSeconds)
    {
        NCreature? creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
        if (creatureNode == null)
        {
            return;
        }

        PrepareAction(creature, creatureNode);
        ActionState state = GetOrCreateState(creature, creatureNode);
        state.ReturnSeconds = RapidAttackTrajectory.AddReturnSeconds(
            state.ReturnSeconds,
            returnSeconds);
        bool isContinuation = state.HasPlayedAction;
        state.StopActiveTween();
        state.HasPlayedAction = true;
        long generation = ++state.Generation;

        float direction = (creature.IsPlayer ? 1f : -1f) * (reverseDirection ? -1f : 1f);
        float peakOffset = distance * direction;
        float currentOffset = creatureNode.Position.X - state.Baseline.X;
        float duration = isContinuation
            ? CombatActionTimingRuntime.ConsecutiveAttackSeconds
            : firstPeakSeconds;
        if (Mathf.IsZeroApprox(duration))
        {
            creatureNode.Position = new Vector2(
                state.Baseline.X + peakOffset,
                creatureNode.Position.Y);
            return;
        }

        Tween tween = creatureNode.CreateTween();
        state.ActiveTween = tween;
        tween.TweenMethod(
                Callable.From<float>(progress =>
                {
                    if (!IsCurrent(creature, state, generation))
                    {
                        return;
                    }

                    float offset = isContinuation
                        ? RapidAttackTrajectory.GetContinuationOffset(
                            progress,
                            currentOffset,
                            peakOffset * 0.5f,
                            peakOffset,
                            outboundCurve)
                        : Mathf.Lerp(currentOffset, peakOffset, outboundCurve(progress));
                    creatureNode.Position = new Vector2(
                        state.Baseline.X + offset,
                        creatureNode.Position.Y);
                }),
                0f,
                1f,
                duration)
            .SetTrans(Tween.TransitionType.Linear);

        bool completed = await TweenPlayback.AwaitCompletion(tween, creatureNode);
        if (completed && IsCurrent(creature, state, generation))
        {
            state.ActiveTween = null;
            creatureNode.Position = new Vector2(
                state.Baseline.X + peakOffset,
                creatureNode.Position.Y);
        }
    }

    public static Task BeginHeldSlowApproach(Creature creature, float duration)
    {
        return PlayAttackToPeak(
            creature,
            NinjaSlayerCombatVisuals.SlowAttackLungeDistance,
            duration,
            FinisherActionTrajectory.SlowProgress,
            returnSeconds: CombatActionTimingRuntime.DamageRecoverySeconds);
    }

    public static void CardGameplaySettled(Creature creature)
    {
        if (!RapidCardPresentationContext.IsActive
            || NinjaSlayerFinisherCinematic.IsMovementOwned(creature)
            || !States.TryGetValue(creature, out ActionState? state)
            || !GodotObject.IsInstanceValid(state.CreatureNode))
        {
            return;
        }

        state.GameplaySettled = true;
        if (VisualTails.ContainsKey(creature))
        {
            return;
        }

        StartReturn(creature, state);
    }

    public static void PrepareAction(Creature creature, NCreature creatureNode)
    {
        ActionState state = GetOrCreateState(creature, creatureNode);
        state.GameplaySettled = false;
        state.StopActiveTween();
        if (VisualTails.Remove(creature, out VisualTailState? tail))
        {
            RapidMotionHandoff? handoff = tail.TryTakeover?.Invoke();
            if (handoff is { } continuation)
            {
                state.AddHandoff(continuation);
            }
            else
            {
                tail.CancelAndRestore();
            }
        }
    }

    public static Vector2 GetBaseline(Creature creature, NCreature creatureNode) =>
        States.TryGetValue(creature, out ActionState? state)
            && ReferenceEquals(state.CreatureNode, creatureNode)
            ? state.Baseline
            : creatureNode.Position;

    private static void StartReturn(Creature creature, ActionState state)
    {
        state.StopActiveTween();
        long generation = ++state.Generation;
        RapidMotionChannel[] channels = state.Channels
            .Where(channel => GodotObject.IsInstanceValid(channel.Target))
            .ToArray();
        if (channels.Length == 0)
        {
            States.Remove(creature);
            return;
        }

        float returnSeconds = state.ReturnSeconds;
        Vector2[] returnStarts = channels.Select(channel => channel.GetPosition()).ToArray();
        if (Mathf.IsZeroApprox(returnSeconds))
        {
            RestoreChannels(channels);
            States.Remove(creature);
            return;
        }

        Tween tween = state.CreatureNode.CreateTween();
        state.ActiveTween = tween;
        tween.TweenMethod(
                Callable.From<float>(progress =>
                {
                    if (!IsCurrent(creature, state, generation))
                    {
                        return;
                    }

                    state.ReturnSeconds = RapidAttackTrajectory.RemainingReturnSeconds(
                        returnSeconds,
                        progress);
                    float eased = Mathf.SmoothStep(0f, 1f, progress);
                    for (int index = 0; index < channels.Length; index++)
                    {
                        RapidMotionChannel channel = channels[index];
                        if (GodotObject.IsInstanceValid(channel.Target))
                        {
                            channel.SetPosition(returnStarts[index].Lerp(channel.Baseline, eased));
                        }
                    }
                }),
                0f,
                1f,
                returnSeconds)
            .SetTrans(Tween.TransitionType.Linear);
        TaskHelper.RunSafely(CompleteReturn(creature, state, generation, tween));
    }

    public static Vector2 ClaimExclusiveBaseline(Creature creature, NCreature creatureNode)
    {
        CancelVisualTail(creature);
        if (!States.Remove(creature, out ActionState? state))
        {
            return creatureNode.Position;
        }

        state.StopActiveTween();
        RestoreChannels(state.Channels.Skip(1).ToArray());
        return state.Baseline;
    }

    public static void CancelAndRestore(Creature creature)
    {
        JumpAnimation.StopForAirChannel(creature);
        HopAnimation.StopForAirChannel(creature);
        CancelVisualTail(creature);
        if (States.Remove(creature, out ActionState? state))
        {
            state.StopAndRestore();
        }

        Participants.Remove(creature);
    }

    public static long RegisterReturnTail(
        Creature creature,
        Func<RapidMotionHandoff?>? tryTakeover,
        Action cancelAndRestore)
    {
        EnsureLifecycle(creature);
        CancelVisualTail(creature);
        long generation = Interlocked.Increment(ref _visualTailGeneration);
        VisualTails[creature] = new VisualTailState(generation, tryTakeover, cancelAndRestore);
        return generation;
    }

    public static void CancelVisualTailForAction(Creature creature)
    {
        if (RapidCardPresentationContext.IsActive)
        {
            CancelVisualTail(creature);
        }
    }

    public static void CompleteVisualTail(Creature creature, long generation)
    {
        if (VisualTails.TryGetValue(creature, out VisualTailState? state)
            && state.Generation == generation)
        {
            VisualTails.Remove(creature);
            if (States.TryGetValue(creature, out ActionState? actionState)
                && actionState.GameplaySettled)
            {
                StartReturn(creature, actionState);
            }
        }
    }

    public static void ResetAll()
    {
        foreach (Creature creature in Participants.ToArray())
        {
            CancelAndRestore(creature);
        }

        Participants.Clear();
        States.Clear();
        VisualTails.Clear();
    }

    private static ActionState GetOrCreateState(Creature creature, NCreature creatureNode)
    {
        EnsureLifecycle(creature);
        if (States.TryGetValue(creature, out ActionState? state)
            && ReferenceEquals(state.CreatureNode, creatureNode)
            && GodotObject.IsInstanceValid(creatureNode))
        {
            return state;
        }

        CancelVisualTail(creature);
        state?.StopAndRestore();
        var created = new ActionState(creatureNode, creatureNode.Position);
        States[creature] = created;
        return created;
    }

    private static void EnsureRoomSubscription()
    {
        NCombatRoom? room = NCombatRoom.Instance;
        CombatManager? combatManager = CombatManager.Instance;
        if (!ReferenceEquals(combatManager, _subscribedCombatManager))
        {
            UnsubscribeCombatManager();
            _subscribedCombatManager = combatManager;
            if (combatManager != null)
            {
                combatManager.CombatEnded += OnCombatFinished;
            }
        }

        if (ReferenceEquals(room, _subscribedRoom))
        {
            return;
        }

        if (_subscribedRoom != null && GodotObject.IsInstanceValid(_subscribedRoom))
        {
            _subscribedRoom.TreeExiting -= OnRoomTreeExiting;
        }

        ResetAll();
        _subscribedRoom = room;
        if (room != null)
        {
            room.TreeExiting += OnRoomTreeExiting;
        }
    }

    private static void OnRoomTreeExiting()
    {
        ResetAll();
        UnsubscribeCombatManager();
        _subscribedRoom = null;
    }

    private static void OnCombatFinished(CombatRoom _) => ResetAll();

    private static void UnsubscribeCombatManager()
    {
        if (_subscribedCombatManager == null)
        {
            return;
        }

        _subscribedCombatManager.CombatEnded -= OnCombatFinished;
        _subscribedCombatManager = null;
    }

    private static async Task CompleteReturn(
        Creature creature,
        ActionState state,
        long generation,
        Tween tween)
    {
        bool completed = await TweenPlayback.AwaitCompletion(tween, state.CreatureNode);
        if (!completed || !IsCurrent(creature, state, generation))
        {
            return;
        }

        state.ActiveTween = null;
        RestoreChannels(state.Channels.ToArray());
        States.Remove(creature);
    }

    private static bool IsCurrent(Creature creature, ActionState state, long generation) =>
        States.TryGetValue(creature, out ActionState? current)
        && ReferenceEquals(current, state)
        && state.Generation == generation
        && GodotObject.IsInstanceValid(state.CreatureNode);

    private static void CancelVisualTail(Creature creature)
    {
        if (VisualTails.Remove(creature, out VisualTailState? state))
        {
            state.CancelAndRestore();
        }
    }

    private static void RestoreChannels(RapidMotionChannel[] channels)
    {
        foreach (RapidMotionChannel channel in channels)
        {
            if (GodotObject.IsInstanceValid(channel.Target))
            {
                channel.SetPosition(channel.Baseline);
            }
        }
    }

    private sealed class ActionState(NCreature creatureNode, Vector2 baseline)
    {
        public NCreature CreatureNode { get; } = creatureNode;
        public Vector2 Baseline { get; } = baseline;
        public Tween? ActiveTween { get; set; }
        public long Generation { get; set; }
        public bool HasPlayedAction { get; set; }
        public bool GameplaySettled { get; set; }
        public float ReturnSeconds { get; set; }
        public List<RapidMotionChannel> Channels { get; } = [RapidMotionChannel.For(creatureNode, baseline)];

        public void AddHandoff(RapidMotionHandoff handoff)
        {
            ReturnSeconds = RapidAttackTrajectory.AddReturnSeconds(
                ReturnSeconds,
                handoff.RemainingSeconds);
            foreach (RapidMotionChannel channel in handoff.Channels)
            {
                if (!Channels.Any(existing => ReferenceEquals(existing.Target, channel.Target)))
                {
                    Channels.Add(channel);
                }
            }
        }

        public void StopActiveTween()
        {
            if (ActiveTween is { } tween && GodotObject.IsInstanceValid(tween) && tween.IsValid())
            {
                tween.Kill();
            }

            ActiveTween = null;
        }

        public void StopAndRestore()
        {
            StopActiveTween();
            RestoreChannels(Channels.ToArray());
        }
    }

    private sealed record VisualTailState(
        long Generation,
        Func<RapidMotionHandoff?>? TryTakeover,
        Action CancelAndRestore);
}

internal sealed record RapidMotionChannel(
    Node Target,
    Func<Vector2> GetPosition,
    Action<Vector2> SetPosition,
    Vector2 Baseline)
{
    public static RapidMotionChannel For(Control node, Vector2 baseline) =>
        new(node, () => node.Position, position => node.Position = position, baseline);

    public static RapidMotionChannel For(Node2D node, Vector2 baseline) =>
        new(node, () => node.Position, position => node.Position = position, baseline);
}

internal sealed record RapidMotionHandoff(
    RapidMotionChannel[] Channels,
    float RemainingSeconds);
