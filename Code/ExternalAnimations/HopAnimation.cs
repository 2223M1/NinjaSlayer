using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Nodes;

namespace NinjaSlayer.Code.ExternalAnimations;

public static class HopAnimation
{
    private static readonly Dictionary<ulong, Vector2> _basePositions = new();
    private static readonly Dictionary<ulong, Tween> _activeTweens = new();

    public static void RegisterBasePosition(Creature creature)
    {
        var anchor = GetHopTarget(creature);
        if (anchor != null)
        {
            var id = anchor.GetInstanceId();
            StopActiveTween(id, anchor);
            _basePositions[id] = anchor.Position;
        }
    }

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
        var actionDuration = 0.10f;

        var tween = creatureNode.CreateTween();
        _activeTweens[id] = tween;

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
        tween.TweenCallback(Callable.From(() =>
        {
            if (_activeTweens.TryGetValue(id, out Tween? activeTween) && activeTween == tween)
            {
                anchor.Position = basePos;
                _activeTweens.Remove(id);
            }
        }));

        await Cmd.Wait(actionDuration);
    }

    private static void StopActiveTween(ulong id, Node2D anchor)
    {
        if (!_activeTweens.Remove(id, out Tween? tween))
        {
            return;
        }

        if (GodotObject.IsInstanceValid(tween))
        {
            tween.Kill();
        }

        if (_basePositions.TryGetValue(id, out Vector2 basePos))
        {
            anchor.Position = basePos;
        }
    }

    private static Node2D? GetHopTarget(Creature creature)
    {
        var visuals = NCombatRoom.Instance?.GetCreatureNode(creature)?.Visuals;
        return NinjaSlayerVisualRig.GetAirborneAnchor(visuals);
    }
}
