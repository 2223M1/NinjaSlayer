using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.ExternalAnimations;

/// <summary>
/// X-cost combo lunge: approach runs alongside the attacks, holds at maximum distance, then returns.
/// Uses slow-attack movement without triggering the SlowAttack animation or audio route.
/// </summary>
public static class XAttackComboMovement
{
    private const float LungeDistance = NinjaSlayerCombatVisuals.SlowAttackLungeDistance;

    private static readonly Dictionary<Creature, ComboMovementState> ComboStates = new();

    public static void BeginCombo(Creature creature, float authoredHitDelay)
    {
        float approachDuration = CombatActionTimingRuntime.Resolve(
            authoredHitDelay,
            Math.Min(authoredHitDelay * 0.5f, 0.25f));
        if (NinjaSlayerFinisherCinematic.TryPlayOwnedAction(
                creature,
                approachDuration,
                out _))
        {
            return;
        }

        if (NinjaSlayerRapidAnimationCoordinator.IsEnabled
            && creature.Player?.Character is INinjaSlayerCharacter)
        {
            ComboStates[creature] = new ComboMovementState(
                NinjaSlayerRapidAnimationCoordinator.BeginHeldSlowApproach(creature, approachDuration));
            return;
        }

        var creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
        if (creatureNode == null)
        {
            return;
        }

        if (ComboStates.Remove(creature, out ComboMovementState? previousState))
        {
            previousState.StopAndRestore();
        }

        var state = new ComboMovementState(creatureNode, creatureNode.Position, approachDuration);
        ComboStates[creature] = state;
        state.ApproachTask = PlayApproach(creature, state);
    }

    private static async Task PlayApproach(Creature creature, ComboMovementState state)
    {
        if (!GodotObject.IsInstanceValid(state.CreatureNode))
        {
            return;
        }

        float direction = creature.IsPlayer ? 1f : -1f;
        if (Mathf.IsZeroApprox(state.ApproachDuration))
        {
            state.CreatureNode.Position = state.BasePosition
                + Vector2.Right * LungeDistance * direction;
            return;
        }

        var tween = state.CreatureNode.CreateTween();
        state.ActiveTween = tween;
        tween.TweenMethod(
            Callable.From<float>(progress =>
            {
                float easedT = FinisherActionTrajectory.SlowProgress(progress);
                float xOffset = Mathf.Lerp(0f, LungeDistance, easedT);
                state.CreatureNode.Position = new Vector2(
                    state.BasePosition.X + xOffset * direction,
                    state.BasePosition.Y);
            }),
            0f,
            1f,
            state.ApproachDuration
        ).SetTrans(Tween.TransitionType.Linear);

        bool completed = await TweenPlayback.AwaitCompletion(tween, state.CreatureNode);
        if (completed
            && ComboStates.TryGetValue(creature, out ComboMovementState? currentState)
            && ReferenceEquals(currentState, state)
            && GodotObject.IsInstanceValid(state.CreatureNode))
        {
            state.CreatureNode.Position = new Vector2(
                state.BasePosition.X + LungeDistance * direction,
                state.BasePosition.Y);
        }
    }

    public static async Task EndCombo(Creature creature)
    {
        if (NinjaSlayerFinisherCinematic.IsMovementOwned(creature))
        {
            if (ComboStates.Remove(creature, out ComboMovementState? ownedState))
            {
                ownedState.StopWithoutRestore();
            }

            await NinjaSlayerFinisherCinematic.WaitForOwnedActionPeak(creature);
            return;
        }

        if (NinjaSlayerRapidAnimationCoordinator.IsEnabled
            && creature.Player?.Character is INinjaSlayerCharacter)
        {
            if (ComboStates.Remove(creature, out ComboMovementState? rapidState))
            {
                await rapidState.ApproachTask;
            }

            return;
        }

        if (!ComboStates.TryGetValue(creature, out ComboMovementState? state))
        {
            return;
        }

        try
        {
            await state.ApproachTask;
            if (!ComboStates.TryGetValue(creature, out ComboMovementState? currentState)
                || !ReferenceEquals(currentState, state)
                || !GodotObject.IsInstanceValid(state.CreatureNode))
            {
                return;
            }

            Vector2 returnStart = state.CreatureNode.Position;
            float returnDuration = CombatActionTimingRuntime.DamageRecoverySeconds;
            if (Mathf.IsZeroApprox(returnDuration))
            {
                state.CreatureNode.Position = state.BasePosition;
                return;
            }

            var tween = state.CreatureNode.CreateTween();
            state.ActiveTween = tween;
            tween.TweenMethod(
                Callable.From<float>(progress =>
                {
                    float easedT = progress * progress * (3f - 2f * progress);
                    state.CreatureNode.Position = returnStart.Lerp(state.BasePosition, easedT);
                }),
                0f,
                1f,
                returnDuration
            ).SetTrans(Tween.TransitionType.Linear);

            await TweenPlayback.AwaitCompletion(tween, state.CreatureNode);
        }
        finally
        {
            if (ComboStates.TryGetValue(creature, out ComboMovementState? currentState)
                && ReferenceEquals(currentState, state))
            {
                ComboStates.Remove(creature);
                state.StopAndRestore();
            }
        }
    }

    private sealed class ComboMovementState
    {
        public ComboMovementState(NCreature creatureNode, Vector2 basePosition, float approachDuration)
        {
            CreatureNode = creatureNode;
            BasePosition = basePosition;
            ApproachDuration = approachDuration;
        }

        public ComboMovementState(Task approachTask)
        {
            ApproachTask = approachTask;
        }

        public NCreature? CreatureNode { get; }
        public Vector2 BasePosition { get; }
        public float ApproachDuration { get; }
        public Task ApproachTask { get; set; } = Task.CompletedTask;
        public Tween? ActiveTween { get; set; }

        public void StopAndRestore()
        {
            StopWithoutRestore();

            if (CreatureNode != null && GodotObject.IsInstanceValid(CreatureNode))
            {
                CreatureNode.Position = BasePosition;
            }
        }

        public void StopWithoutRestore()
        {
            if (ActiveTween is { } tween && GodotObject.IsInstanceValid(tween) && tween.IsValid())
            {
                tween.Kill();
            }

            ActiveTween = null;
        }
    }
}
