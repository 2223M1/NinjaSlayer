using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Lifecycle;
using NinjaSlayer.Code.Nodes;

namespace NinjaSlayer.Code.ExternalAnimations;

public static class JumpAnimation
{
    private const float AnimationDuration = 0.7f;
    private const float ActionDuration = 0.25f;
    private const float JumpHeight = 150f;
    private static readonly Dictionary<Creature, JumpState> ActiveTweens = [];

    internal static bool IsActive(Creature creature) =>
        ActiveTweens.TryGetValue(creature, out JumpState? state)
        && state.Tween.IsValid()
        && state.Tween.IsRunning();

    internal static void StopForFinisher(Creature creature)
    {
        StopForAirChannel(creature);
        HopAnimation.StopForAirChannel(creature);
    }

    internal static void StopForAirChannel(Creature creature)
    {
        if (ActiveTweens.Remove(creature, out JumpState? state))
        {
            state.StopAndRestore();
        }
    }

    public static async Task Play(Creature creature)
    {
        var creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
        if (creatureNode == null)
        {
            return;
        }

        var visuals = creatureNode.Visuals;
        if (visuals == null)
        {
            return;
        }

        Node2D target = NinjaSlayerVisualRig.GetAirborneAnchor(visuals) ?? visuals;
        bool rapid = RapidCardPresentationContext.IsActive;
        if (rapid)
        {
            NinjaSlayerRapidAnimationCoordinator.PrepareAction(creature, creatureNode);
        }
        HopAnimation.StopForAirChannel(creature);
        if (ActiveTweens.Remove(creature, out JumpState? previous))
        {
            previous.StopAndRestore();
        }

        Vector2 originalPos = target.Position;
        var tween = creatureNode.CreateTween();
        var state = new JumpState(tween, target, originalPos);
        tween.TweenMethod(
                Callable.From<float>(progress =>
                {
                    state.Progress = progress;
                    float animY = 4f * JumpHeight * progress * (1f - progress);
                    target.Position = new Vector2(originalPos.X, originalPos.Y - animY);
                }),
                0f,
                1f,
                AnimationDuration)
            .SetTrans(Tween.TransitionType.Linear);

        ActiveTweens[creature] = state;
        if (rapid)
        {
            state.TailGeneration = NinjaSlayerRapidAnimationCoordinator.RegisterReturnTail(
                creature,
                () => Takeover(creature, state),
                () => StopForAirChannel(creature));
        }
        _ = TaskHelper.RunSafely(ClearWhenFinished(creature, creatureNode, state));
        await Cmd.Wait(ActionDuration);
    }

    private static async Task ClearWhenFinished(Creature creature, Node owner, JumpState state)
    {
        try
        {
            await TweenPlayback.AwaitCompletion(state.Tween, owner);
        }
        finally
        {
            if (ActiveTweens.TryGetValue(creature, out JumpState? active)
                && ReferenceEquals(active, state))
            {
                ActiveTweens.Remove(creature);
                state.Restore();
                if (state.TailGeneration is { } generation)
                {
                    NinjaSlayerRapidAnimationCoordinator.CompleteVisualTail(creature, generation);
                }
            }
        }
    }

    private static RapidMotionHandoff? Takeover(Creature creature, JumpState state)
    {
        if (!ActiveTweens.Remove(creature, out JumpState? active)
            || !ReferenceEquals(active, state))
        {
            return null;
        }

        state.StopWithoutRestore();
        return new(
            [RapidMotionChannel.For(state.Target, state.OriginalPosition)],
            RapidAttackTrajectory.RemainingReturnSeconds(AnimationDuration, state.Progress));
    }

    private sealed class JumpState
    {
        public JumpState(Tween tween, Node2D target, Vector2 originalPosition)
        {
            Tween = tween;
            Target = target;
            OriginalPosition = originalPosition;
        }

        public Tween Tween { get; }
        public Node2D Target { get; }
        public Vector2 OriginalPosition { get; }
        public float Progress { get; set; }
        public long? TailGeneration { get; set; }

        public void StopWithoutRestore()
        {
            if (Tween.IsValid())
            {
                Tween.Kill();
            }
        }

        public void StopAndRestore()
        {
            StopWithoutRestore();

            Restore();
        }

        public void Restore()
        {
            if (GodotObject.IsInstanceValid(Target))
            {
                Target.Position = OriginalPosition;
            }
        }
    }
}
