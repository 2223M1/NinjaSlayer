using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Nodes;

namespace NinjaSlayer.Code.ExternalAnimations;

public static class StaggerAnimation
{
    private const float StaggerDuration = 0.3f;
    private const float StaggerDistance = 20f;
    internal const float DefaultRotationDegrees = -15f;
    internal const float MirroredRotationDegrees = 15f;

    private static readonly Dictionary<Creature, StaggerState> ActiveStates = [];

    public static bool IsActive(Creature creature) => ActiveStates.ContainsKey(creature);

    public static async Task Play(
        Creature creature,
        float rotationDegrees = DefaultRotationDegrees)
    {
        if (ActiveStates.Remove(creature, out StaggerState? previous))
        {
            previous.StopAndRestore();
        }

        var creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
        if (creatureNode == null)
        {
            return;
        }

        var visuals = creatureNode.Visuals;
        var bodyAnchor = NinjaSlayerVisualRig.GetAirborneAnchor(visuals)
            ?? NinjaSlayerVisualRig.GetBodySprite(visuals);
        var tween = creatureNode.CreateTween();
        var state = new StaggerState(
            tween,
            creatureNode,
            bodyAnchor,
            creatureNode.Position,
            bodyAnchor?.RotationDegrees ?? 0f,
            creature.IsPlayer ? -1f : 1f,
            rotationDegrees);
        ActiveStates[creature] = state;

        try
        {
            tween.TweenMethod(
                Callable.From<float>(state.Apply),
                0f,
                1f,
                StaggerDuration
            ).SetTrans(Tween.TransitionType.Linear);
            await TweenPlayback.AwaitCompletion(tween, creatureNode);
        }
        finally
        {
            if (ActiveStates.TryGetValue(creature, out StaggerState? active)
                && ReferenceEquals(active, state))
            {
                ActiveStates.Remove(creature);
                state.StopAndRestore();
            }
        }
    }

    public static void Reset()
    {
        foreach (Creature creature in ActiveStates.Keys.ToArray())
        {
            Reset(creature);
        }
    }

    public static void Reset(Creature creature)
    {
        if (ActiveStates.Remove(creature, out StaggerState? state))
        {
            state.StopAndRestore();
        }
    }

    private sealed class StaggerState(
        Tween tween,
        Control creatureNode,
        Node2D? bodyAnchor,
        Vector2 originalPosition,
        float originalBodyRotation,
        float direction,
        float rotationDegrees)
    {
        private bool _stopped;

        public Tween Tween { get; } = tween;

        public void Apply(float progress)
        {
            if (_stopped || !GodotObject.IsInstanceValid(creatureNode))
            {
                return;
            }

            float easedProgress = progress * progress;
            float xOffset = Mathf.Lerp(StaggerDistance, 0f, easedProgress) * direction;
            creatureNode.Position = new Vector2(originalPosition.X + xOffset, originalPosition.Y);
            if (bodyAnchor != null && GodotObject.IsInstanceValid(bodyAnchor))
            {
                bodyAnchor.RotationDegrees = originalBodyRotation
                    + Mathf.Lerp(rotationDegrees, 0f, easedProgress);
            }
        }

        public void StopAndRestore()
        {
            if (_stopped)
            {
                return;
            }

            _stopped = true;
            try
            {
                if (GodotObject.IsInstanceValid(Tween) && Tween.IsValid())
                {
                    Tween.Kill();
                }
            }
            finally
            {
                if (GodotObject.IsInstanceValid(creatureNode))
                {
                    creatureNode.Position = originalPosition;
                }

                if (bodyAnchor != null && GodotObject.IsInstanceValid(bodyAnchor))
                {
                    bodyAnchor.RotationDegrees = originalBodyRotation;
                }
            }
        }
    }
}
