using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Nodes;

namespace NinjaSlayer.Code.ExternalAnimations;

public static class HopAnimation
{
    private static readonly Dictionary<ulong, Vector2> _basePositions = new();

    public static void RegisterBasePosition(Creature creature)
    {
        var anchor = GetHopTarget(creature);
        if (anchor != null)
        {
            _basePositions[anchor.GetInstanceId()] = anchor.Position;
        }
    }

    public static void SyncBasePosition(Creature creature, Vector2 basePosition)
    {
        var anchor = GetHopTarget(creature);
        if (anchor != null)
        {
            _basePositions[anchor.GetInstanceId()] = basePosition;
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

        var id = anchor.GetInstanceId();
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
        var actionDuration = 0.10f;

        var tween = creatureNode.CreateTween();

        tween.TweenMethod(
            Callable.From<float>(t =>
            {
                var yOffset = Mathf.Sin(t * Mathf.Pi) * hopHeight;
                anchor.Position = new Vector2(basePos.X, basePos.Y - yOffset);
            }),
            0f,
            1f,
            animationDuration
        ).SetTrans(Tween.TransitionType.Linear);

        await Cmd.Wait(actionDuration);
    }

    private static Node2D? GetHopTarget(Creature creature)
    {
        var visuals = NCombatRoom.Instance?.GetCreatureNode(creature)?.Visuals;
        return NinjaSlayerVisualRig.GetAirborneAnchor(visuals);
    }
}
