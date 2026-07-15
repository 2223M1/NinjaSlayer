using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace NinjaSlayer.Code.ExternalAnimations;

public static class ShakeAnimation
{
    private sealed class ActiveShake
    {
        public required Tween Tween { get; init; }
        public required Vector2 OriginalPosition { get; init; }
        public required Action OnTreeExiting { get; init; }
    }

    private const float ShakeSpeed = 150f;
    private const float ShakeThreshold = 8f;
    private const float DefaultDuration = 1f;

    private static readonly Dictionary<Node2D, ActiveShake> ActiveShakes = [];

    public static async Task Play(Creature creature, float awaitDuration = 1.0f, float? totalDuration = null)
    {
        var actualTotalDuration = totalDuration ?? awaitDuration;
        if (!Start(creature, actualTotalDuration))
        {
            return;
        }

        await Cmd.Wait(awaitDuration);
    }

    public static void PlayNonBlocking(Creature creature, float totalDuration = DefaultDuration)
    {
        Start(creature, totalDuration);
    }

    private static bool Start(Creature creature, float totalDuration)
    {
        var creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
        if (creatureNode?.Visuals is not Node2D visuals || totalDuration <= 0f)
        {
            return false;
        }

        CancelActiveShake(visuals);

        var originalPos = visuals.Position;
        var elapsed = 0f;
        var shakeToggle = true;
        var animX = 0f;

        var tween = creatureNode.CreateTween();
        ActiveShake? activeShake = null;
        Action onTreeExiting = () => Finish(visuals, activeShake, restorePosition: false);
        activeShake = new ActiveShake
        {
            Tween = tween,
            OriginalPosition = originalPos,
            OnTreeExiting = onTreeExiting
        };
        ActiveShakes[visuals] = activeShake;
        visuals.TreeExiting += onTreeExiting;

        tween.TweenMethod(
            Callable.From<float>(t =>
            {
                var delta = t * totalDuration - elapsed;
                elapsed = t * totalDuration;

                if (shakeToggle)
                {
                    animX += ShakeSpeed * delta;
                    if (animX > ShakeThreshold)
                    {
                        shakeToggle = false;
                    }
                }
                else
                {
                    animX -= ShakeSpeed * delta;
                    if (animX < -ShakeThreshold)
                    {
                        shakeToggle = true;
                    }
                }

                visuals.Position = new Vector2(originalPos.X + animX, originalPos.Y);
            }),
            0f,
            1f,
            totalDuration
        ).SetTrans(Tween.TransitionType.Linear);

        tween.Finished += () => Finish(visuals, activeShake, restorePosition: true);
        return true;
    }

    private static void CancelActiveShake(Node2D visuals)
    {
        if (!ActiveShakes.Remove(visuals, out ActiveShake? activeShake))
        {
            return;
        }

        activeShake.Tween.Kill();
        visuals.TreeExiting -= activeShake.OnTreeExiting;
        visuals.Position = activeShake.OriginalPosition;
    }

    private static void Finish(Node2D visuals, ActiveShake? activeShake, bool restorePosition)
    {
        if (activeShake is null
            || !ActiveShakes.TryGetValue(visuals, out ActiveShake? current)
            || !ReferenceEquals(current, activeShake))
        {
            return;
        }

        ActiveShakes.Remove(visuals);
        activeShake.Tween.Kill();
        visuals.TreeExiting -= activeShake.OnTreeExiting;
        if (restorePosition && GodotObject.IsInstanceValid(visuals))
        {
            visuals.Position = activeShake.OriginalPosition;
        }
    }
}
