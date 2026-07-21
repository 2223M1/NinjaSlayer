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
    private static readonly Dictionary<Creature, Tween> ActiveTweens = [];

    internal static bool IsActive(Creature creature) =>
        ActiveTweens.TryGetValue(creature, out Tween? tween)
        && tween.IsValid()
        && tween.IsRunning();

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

        ActiveTweens[creature] = tween;
        _ = TaskHelper.RunSafely(ClearWhenFinished(creature, creatureNode, tween));
        await Cmd.Wait(ActionDuration);
    }

    private static async Task ClearWhenFinished(Creature creature, Node owner, Tween tween)
    {
        try
        {
            await owner.ToSignal(tween, Tween.SignalName.Finished);
        }
        finally
        {
            if (ActiveTweens.TryGetValue(creature, out Tween? activeTween)
                && ReferenceEquals(activeTween, tween))
            {
                ActiveTweens.Remove(creature);
            }
        }
    }
}
