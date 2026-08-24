using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Lifecycle;
using NinjaSlayer.Code.Nodes;

namespace NinjaSlayer.Code.ExternalAnimations;

public static class HopAnimation
{
    private static readonly Dictionary<ulong, Vector2> _basePositions = new();
    private static readonly Dictionary<ulong, HopState> _activeStates = new();

    public static void SyncBasePosition(Creature creature, Vector2 basePosition)
    {
        var anchor = GetHopTarget(creature);
        if (anchor != null)
        {
            var id = anchor.GetInstanceId();
            StopActiveTween(id, anchor);
            _basePositions[id] = basePosition;
            anchor.Position = basePosition;
        }
    }

    public static async Task Play(Creature creature)
    {
        var creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
        if (creatureNode == null)
        {
            return;
        }

        var anchor = GetHopTarget(creature);
        if (anchor == null)
        {
            return;
        }

        bool rapid = RapidCardPresentationContext.IsActive;
        if (rapid)
        {
            NinjaSlayerRapidAnimationCoordinator.PrepareAction(creature, creatureNode);
        }
        JumpAnimation.StopForAirChannel(creature);
        var id = anchor.GetInstanceId();
        StopActiveTween(id, anchor);
        if (!_basePositions.TryGetValue(id, out var basePos) || SoarVisualState.IsAirborne(creature))
        {
            if (SoarVisualState.IsAirborne(creature))
            {
                SoarVisualState.EnforceAirbornePosition(creature);
            }

            basePos = anchor.Position;
            _basePositions[id] = basePos;
        }
        else
        {
            anchor.Position = basePos;
        }

        var hopHeight = 60f;
        var animationDuration = 0.28f;
        float actionDuration = CombatActionTimingRuntime.CastSeconds;

        if (Mathf.IsZeroApprox(actionDuration))
        {
            anchor.Position = basePos;
            return;
        }

        var tween = creatureNode.CreateTween();
        var state = new HopState(id, tween, anchor, basePos);
        _activeStates[id] = state;

        tween.TweenMethod(
            Callable.From<float>(t =>
            {
                state.Progress = t;
                var yOffset = Mathf.Sin(t * Mathf.Pi) * hopHeight;
                anchor.Position = new Vector2(basePos.X, basePos.Y - yOffset);
            }),
            0f,
            1f,
            animationDuration
        ).SetTrans(Tween.TransitionType.Linear);
        tween.TweenCallback(Callable.From(() =>
        {
            if (_activeStates.TryGetValue(id, out HopState? activeState)
                && ReferenceEquals(activeState, state))
            {
                anchor.Position = basePos;
                _activeStates.Remove(id);
                if (state.TailGeneration is { } generation)
                {
                    NinjaSlayerRapidAnimationCoordinator.CompleteVisualTail(creature, generation);
                }
            }
        }));

        if (rapid)
        {
            state.TailGeneration = NinjaSlayerRapidAnimationCoordinator.RegisterReturnTail(
                creature,
                () => Takeover(state),
                () => StopForAirChannel(creature));
        }

        await Cmd.Wait(actionDuration);
    }

    internal static void StopForAirChannel(Creature creature)
    {
        Node2D? anchor = GetHopTarget(creature);
        if (anchor != null)
        {
            StopActiveTween(anchor.GetInstanceId(), anchor);
        }
    }

    private static void StopActiveTween(ulong id, Node2D anchor)
    {
        if (!_activeStates.Remove(id, out HopState? state))
        {
            return;
        }

        state.StopWithoutRestore();

        if (_basePositions.TryGetValue(id, out Vector2 basePos))
        {
            anchor.Position = basePos;
        }
    }

    private static RapidMotionHandoff? Takeover(HopState state)
    {
        if (!_activeStates.Remove(state.Id, out HopState? active)
            || !ReferenceEquals(active, state))
        {
            return null;
        }

        state.StopWithoutRestore();
        return new(
            [RapidMotionChannel.For(state.Anchor, state.BasePosition)],
            RapidAttackTrajectory.RemainingReturnSeconds(0.28f, state.Progress));
    }

    private sealed class HopState(
        ulong id,
        Tween tween,
        Node2D anchor,
        Vector2 basePosition)
    {
        public ulong Id { get; } = id;
        public Tween Tween { get; } = tween;
        public Node2D Anchor { get; } = anchor;
        public Vector2 BasePosition { get; } = basePosition;
        public float Progress { get; set; }
        public long? TailGeneration { get; set; }

        public void StopWithoutRestore()
        {
            if (GodotObject.IsInstanceValid(Tween) && Tween.IsValid())
            {
                Tween.Kill();
            }
        }
    }

    private static Node2D? GetHopTarget(Creature creature)
    {
        var visuals = NCombatRoom.Instance?.GetCreatureNode(creature)?.Visuals;
        return NinjaSlayerVisualRig.GetAirborneAnchor(visuals);
    }
}
