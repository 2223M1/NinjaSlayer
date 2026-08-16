using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Rooms;
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
        NinjaSlayerRapidAnimationCoordinator.CancelVisualTailForAction(creature);
        HopAnimation.StopForAirChannel(creature);
        if (ActiveTweens.Remove(creature, out JumpState? previous))
        {
            previous.StopAndRestore();
        }

        Vector2 originalPos = target.Position;
        var tween = creatureNode.CreateTween();
        tween.TweenMethod(
                Callable.From<float>(progress =>
                {
                    float animY = 4f * JumpHeight * progress * (1f - progress);
                    target.Position = new Vector2(originalPos.X, originalPos.Y - animY);
                }),
                0f,
                1f,
                AnimationDuration)
            .SetTrans(Tween.TransitionType.Linear);

        var state = new JumpState(tween, target, originalPos);
        ActiveTweens[creature] = state;
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
            }
        }
    }

    private sealed class JumpState(Tween tween, Node2D target, Vector2 originalPosition)
    {
        public Tween Tween { get; } = tween;

        public void StopAndRestore()
        {
            if (Tween.IsValid())
            {
                Tween.Kill();
            }

            Restore();
        }

        public void Restore()
        {
            if (GodotObject.IsInstanceValid(target))
            {
                target.Position = originalPosition;
            }
        }
    }
}
