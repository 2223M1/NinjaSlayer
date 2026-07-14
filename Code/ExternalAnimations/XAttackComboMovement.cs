using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.ExternalAnimations;

/// <summary>
/// X-cost combo lunge: approach runs alongside the attacks, holds at maximum distance, then returns.
/// Uses slow-attack movement without triggering the SlowAttack animation or audio route.
/// </summary>
public static class XAttackComboMovement
{
    private const float ApproachDuration = NinjaSlayerCombatVisuals.SlowAttackLungeDuration;
    private const float ReturnDuration = NinjaSlayerCombatVisuals.SlowAttackLungeDuration;
    private const float LungeDistance = NinjaSlayerCombatVisuals.SlowAttackLungeDistance;

    private static readonly Dictionary<Creature, ComboMovementState> ComboStates = new();

    public static void BeginCombo(Creature creature)
    {
        var creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
        if (creatureNode == null)
        {
            return;
        }

        if (ComboStates.Remove(creature, out ComboMovementState? previousState))
        {
            previousState.StopAndRestore();
        }

        var state = new ComboMovementState(creatureNode, creatureNode.Position);
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
        var tween = state.CreatureNode.CreateTween();
        state.ActiveTween = tween;
        tween.TweenMethod(
            Callable.From<float>(progress =>
            {
                float easedT = Mathf.Pow(progress, 10f);
                float xOffset = Mathf.Lerp(0f, LungeDistance, easedT);
                state.CreatureNode.Position = new Vector2(
                    state.BasePosition.X + xOffset * direction,
                    state.BasePosition.Y);
            }),
            0f,
            1f,
            ApproachDuration
        ).SetTrans(Tween.TransitionType.Linear);

        await state.CreatureNode.ToSignal(tween, Tween.SignalName.Finished);
        if (GodotObject.IsInstanceValid(state.CreatureNode))
        {
            state.CreatureNode.Position = new Vector2(
                state.BasePosition.X + LungeDistance * direction,
                state.BasePosition.Y);
        }
    }

    public static async Task EndCombo(Creature creature)
    {
        if (!ComboStates.TryGetValue(creature, out ComboMovementState? state))
        {
            return;
        }

        try
        {
            await state.ApproachTask;
            if (!GodotObject.IsInstanceValid(state.CreatureNode))
            {
                return;
            }

            Vector2 returnStart = state.CreatureNode.Position;
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
                ReturnDuration
            ).SetTrans(Tween.TransitionType.Linear);

            await state.CreatureNode.ToSignal(tween, Tween.SignalName.Finished);
        }
        finally
        {
            if (ComboStates.TryGetValue(creature, out ComboMovementState? currentState)
                && ReferenceEquals(currentState, state))
            {
                ComboStates.Remove(creature);
            }

            state.StopAndRestore();
        }
    }

    private sealed class ComboMovementState(NCreature creatureNode, Vector2 basePosition)
    {
        public NCreature CreatureNode { get; } = creatureNode;
        public Vector2 BasePosition { get; } = basePosition;
        public Task ApproachTask { get; set; } = Task.CompletedTask;
        public Tween? ActiveTween { get; set; }

        public void StopAndRestore()
        {
            if (ActiveTween is { } tween && GodotObject.IsInstanceValid(tween) && tween.IsValid())
            {
                tween.Kill();
            }

            if (GodotObject.IsInstanceValid(CreatureNode))
            {
                CreatureNode.Position = BasePosition;
            }
        }
    }
}
