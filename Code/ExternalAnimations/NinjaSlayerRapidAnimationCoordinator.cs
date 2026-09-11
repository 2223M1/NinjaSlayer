using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Lifecycle;
using NinjaSlayer.Code.Nodes;
using MegaCrit.Sts2.Core.Entities.Cards;
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
        float returnSeconds = RapidAttackTrajectory.ReturnSeconds,
        bool useConsecutiveGate = true,
        bool heldTornado = false)
    {
        NCreature? creatureNode = creature.GetCreatureNode();
        if (creatureNode == null)
        {
            return;
        }

        PrepareAction(creature, creatureNode);
        ActionState state = GetOrCreateState(creature, creatureNode);
        NinjaSlayerAimPose? pose = NinjaSlayerAimPose.Get(creature);
        Creature? target = NinjaSlayerAttackExecution.Target ?? NinjaSlayerAimPose.Focus(creature);
        if (pose != null && !heldTornado)
        {
            if (!state.HasPlayedAction || !ReferenceEquals(state.LastTarget, target))
                state.ForwardSign = target?.GetCreatureNode() is { } node
                    && node.GlobalPosition.X < creatureNode.GlobalPosition.X ? -1f : 1f;
            state.LastTarget = target;
            pose.AttackForwardSign = state.ForwardSign;
            pose.BeginAction(target);
            await pose.PrepareKick(NinjaSlayerAttackExecution.CurrentPlay);
            if (creature.IsDead || !States.TryGetValue(creature, out ActionState? current)
                || !ReferenceEquals(current, state) || pose.IsExclusive)
                return;
        }
        state.ReturnSeconds = Math.Max(state.ReturnSeconds, returnSeconds);
        bool isContinuation = state.HasPlayedAction;
        if (!isContinuation) state.BaseOffset = pose?.Travel ?? Vector2.Zero;
        state.HasPlayedAction = true;

        float direction = creature.IsPlayer ? 1f : -1f;
        CardPlay? play = NinjaSlayerAttackExecution.CurrentPlay;
        bool samePlay = play != null && ReferenceEquals(state.LastPlay, play);
        state.LastPlay = play;
        float duration = useConsecutiveGate && pose?.IsTornado != true && isContinuation && !NinjaSlayerAttackExecution.IsMultiHit && !samePlay
            ? CombatActionTimingRuntime.ConsecutiveAttackSeconds
            : firstPeakSeconds;
        NinjaSlayerShadowController.Get(creature)?.BeginAction(
            distance >= NinjaSlayerCombatVisuals.SlowAttackLungeDistance ? ShadowActionKind.SlowAttack : ShadowActionKind.Attack,
            duration, returnSeconds, hold: true);
        var motion = new AttackMotion();
        state.Motions.Add(motion);
        float forwardSign = heldTornado ? direction : state.ForwardSign;
        Vector2 initialDirection = pose?.DirectionLocal() ?? Vector2.Right * direction;
        void Apply(float progress)
        {
            Vector2 attackDirection = initialDirection;
            if (pose != null && target?.GetCreatureNode() is { } targetNode)
            {
                Vector2 delta = targetNode.Visuals.VfxSpawnPosition.GetGlobalTransformWithCanvas().Origin - pose.CoreCanvas;
                delta.X = Math.Abs(delta.X) * forwardSign;
                Vector2 localDelta = creatureNode.GetParent<CanvasItem>().GetGlobalTransformWithCanvas().AffineInverse().BasisXform(delta);
                if (localDelta.LengthSquared() > 0.0001f) attackDirection = localDelta.Normalized();
            }
            Vector2 peakOffset = attackDirection * distance * (reverseDirection ? -1f : 1f);
            motion.Offset = (peakOffset - (heldTornado ? state.BaseOffset : Vector2.Zero)) * outboundCurve(progress);
            Vector2 offset = state.BaseOffset;
            foreach (AttackMotion contribution in state.Motions) offset += contribution.Offset;
            if (pose != null)
                pose.SetTravel(offset, outboundCurve(progress));
            else
                creatureNode.Position = state.Baseline + offset;
        }
        if (Mathf.IsZeroApprox(duration))
        {
            Apply(1f);
            motion.Completed = true;
            return;
        }

        Tween tween = creatureNode.CreateTween();
        motion.Tween = tween;
        tween.TweenMethod(
                Callable.From<float>(progress =>
                {
                    if (!IsCurrentState(creature, state))
                    {
                        return;
                    }

                    Apply(progress);
                }),
                0f,
                1f,
                duration)
            .SetTrans(Tween.TransitionType.Linear);

        bool completed = await TweenPlayback.AwaitCompletion(tween, creatureNode);
        if (completed && IsCurrentState(creature, state))
        {
            Apply(1f);
            motion.Completed = true;
            motion.Tween = null;
            if (state.GameplaySettled) StartReturn(creature, state);
        }
    }

    public static Task BeginHeldSlowApproach(Creature creature, float duration)
    {
        return PlayAttackToPeak(
            creature,
            NinjaSlayerCombatVisuals.SlowAttackLungeDistance,
            duration,
            FinisherActionTrajectory.SlowProgress,
            returnSeconds: CombatActionTimingRuntime.DamageRecoverySeconds,
            heldTornado: true);
    }

    public static void CardGameplaySettled(Creature creature)
    {
        if (RapidCardPresentationContext.IsActive)
            NinjaSlayerAimPose.Get(creature)?.CancelPendingCharge(RapidCardPresentationContext.CurrentCard);
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
        if (state.ActiveTween != null)
        {
            state.BaseOffset = NinjaSlayerAimPose.Get(creature)?.Travel
                ?? creatureNode.Position - state.Baseline;
            state.Motions.Clear();
            state.StopActiveTween();
            state.Generation++;
        }
        if (VisualTails.TryGetValue(creature, out VisualTailState? tail) && !tail.IndependentAirChannel)
        {
            VisualTails.Remove(creature);
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
        NinjaSlayerShadowController.Get(creature)?.BeginReturn(state.ReturnSeconds);
        if (state.Motions.Any(motion => !motion.Completed)) return;
        state.StopActiveTween();
        NinjaSlayerAimPose? pose = NinjaSlayerAimPose.Get(creature);
        pose?.BeginReturn();
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
            pose?.ApplyReturn(1f);
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
                    pose?.ApplyReturn(eased);
                }),
                0f,
                1f,
                returnSeconds)
            .SetTrans(Tween.TransitionType.Linear);
        TaskHelper.RunSafely(CompleteReturn(creature, state, generation, tween));
    }

    public static Vector2 ClaimExclusiveBaseline(Creature creature, NCreature creatureNode)
    {
        NinjaSlayerShadowController.Get(creature)?.ResetAction();
        if (VisualTails.Remove(creature, out VisualTailState? tail))
        {
            RapidMotionHandoff? handoff = tail.TryTakeover?.Invoke();
            if (handoff != null && States.TryGetValue(creature, out ActionState? ownedState))
                ownedState.AddHandoff(handoff);
            else if (handoff == null)
                tail.CancelAndRestore();
        }
        if (!States.Remove(creature, out ActionState? state))
        {
            return creatureNode.Position;
        }

        state.StopActiveTween();
        state.StopMotions();
        NinjaSlayerAimPose.Get(creature)?.OwnAirborneReturn(state.Channels.Skip(1).ToArray());
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
        NinjaSlayerAimPose.Get(creature)?.Reset();
        NinjaSlayerShadowController.Get(creature)?.ResetAction();
    }

    public static long RegisterReturnTail(
        Creature creature,
        Func<RapidMotionHandoff?>? tryTakeover,
        Action cancelAndRestore,
        bool independentAirChannel = false)
    {
        EnsureLifecycle(creature);
        CancelVisualTail(creature);
        long generation = Interlocked.Increment(ref _visualTailGeneration);
        VisualTails[creature] = new VisualTailState(generation, tryTakeover, cancelAndRestore, independentAirChannel);
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
        NinjaSlayerAimPose.Get(creature)?.ApplyReturn(1f);
        States.Remove(creature);
    }

    private static bool IsCurrent(Creature creature, ActionState state, long generation) =>
        IsCurrentState(creature, state)
        && state.Generation == generation;

    private static bool IsCurrentState(Creature creature, ActionState state) =>
        States.TryGetValue(creature, out ActionState? current)
        && ReferenceEquals(current, state) && GodotObject.IsInstanceValid(state.CreatureNode);

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
        public Vector2 BaseOffset { get; set; }
        public float ForwardSign { get; set; }
        public Creature? LastTarget { get; set; }
        public List<AttackMotion> Motions { get; } = [];
        public CardPlay? LastPlay { get; set; }
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
            StopMotions();
            RestoreChannels(Channels.ToArray());
        }

        public void StopMotions()
        {
            foreach (AttackMotion motion in Motions)
                if (motion.Tween is { } tween && tween.IsValid()) tween.Kill();
            Motions.Clear();
        }
    }

    private sealed class AttackMotion
    {
        internal Vector2 Offset;
        internal Tween? Tween;
        internal bool Completed;
    }

    private sealed record VisualTailState(
        long Generation,
        Func<RapidMotionHandoff?>? TryTakeover,
        Action CancelAndRestore,
        bool IndependentAirChannel);
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
