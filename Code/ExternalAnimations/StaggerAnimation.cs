using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.Code.ExternalAnimations;

public static class StaggerAnimation
{
    private const float StaggerDuration = 0.3f;
    private const float StaggerDistance = 20f;
    internal const float DefaultRotationDegrees = -15f;
    internal const float MirroredRotationDegrees = 15f;

    private static readonly Dictionary<Creature, StaggerState> ActiveStates = [];

    public static bool IsActive(Creature creature) => ActiveStates.ContainsKey(creature)
        || NinjaSlayerAimPose.Get(creature)?.HasHurt == true;

    internal static Action? PauseCurrent(Creature creature)
    {
        if (NinjaSlayerAimPose.Get(creature) is { } pose) return pose.PauseHurt();
        if (!ActiveStates.TryGetValue(creature, out StaggerState? state)) return null;
        state.Pause();
        return () =>
        {
            if (ActiveStates.TryGetValue(creature, out StaggerState? current) && ReferenceEquals(current, state))
                state.Resume();
        };
    }

    internal static bool TryTakeover(Creature creature, out HandoffLease? lease)
    {
        if (!ActiveStates.Remove(creature, out StaggerState? state))
        {
            lease = null;
            return false;
        }

        lease = state.TransferOwnership();
        return true;
    }

    public static async Task Play(
        Creature creature,
        float rotationDegrees = DefaultRotationDegrees)
    {
        if (NinjaSlayerAimPose.Get(creature) is { } pose)
        {
            var motion = pose.BeginVisualMotion(NinjaSlayerAimPose.MotionKind.Hurt,
                CombatActionTimingRuntime.Resolve(StaggerDuration, StaggerDuration * 0.5f));
            pose.SyncNow();
            if (motion != null) await motion.Completion;
            return;
        }
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
        NinjaSlayerShadowController.Get(creature)?.BeginAction(ShadowActionKind.Hurt, 0f, StaggerDuration);
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
        NinjaSlayerAimPose.Get(creature)?.ClearHurt();
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

        public void Pause()
        {
            if (_stopped || !Tween.IsValid()) return;
            if (Tween.GetTotalElapsedTime() == 0d) Apply(0f);
            Tween.SetSpeedScale(0f);
        }

        public void Resume()
        {
            if (!_stopped && Tween.IsValid()) Tween.SetSpeedScale(1f);
        }

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

        public HandoffLease TransferOwnership()
        {
            if (!_stopped)
            {
                _stopped = true;
                if (GodotObject.IsInstanceValid(Tween) && Tween.IsValid())
                {
                    Tween.Kill();
                }
            }

            return new HandoffLease(
                creatureNode,
                bodyAnchor,
                originalPosition,
                originalBodyRotation,
                creatureNode.Position,
                bodyAnchor?.RotationDegrees ?? originalBodyRotation);
        }
    }

    internal sealed class HandoffLease(
        Control creatureNode,
        Node2D? bodyAnchor,
        Vector2 baselinePosition,
        float baselineBodyRotation,
        Vector2 currentPosition,
        float currentBodyRotation)
    {
        public Control CreatureNode { get; } = creatureNode;
        public Node2D? BodyAnchor { get; } = bodyAnchor;
        public Vector2 BaselinePosition { get; } = baselinePosition;
        public float BaselineBodyRotation { get; } = baselineBodyRotation;
        public Vector2 CurrentPosition { get; } = currentPosition;
        public float CurrentBodyRotation { get; } = currentBodyRotation;

        public void Restore()
        {
            if (GodotObject.IsInstanceValid(CreatureNode))
            {
                CreatureNode.Position = BaselinePosition;
            }

            if (BodyAnchor != null && GodotObject.IsInstanceValid(BodyAnchor))
            {
                BodyAnchor.RotationDegrees = BaselineBodyRotation;
            }
        }
    }
}
