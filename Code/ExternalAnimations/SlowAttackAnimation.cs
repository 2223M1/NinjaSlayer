using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.ExternalAnimations;

public static class SlowAttackAnimation
{
    private const float AnimationDuration = NinjaSlayerCombatVisuals.SlowAttackLungeDuration * 2f;
    private const float ActionDuration = NinjaSlayerCombatVisuals.SlowAttackLungeDuration;
    private const float LungeDistance = NinjaSlayerCombatVisuals.SlowAttackLungeDistance;

    public static async Task Play(Creature creature)
    {
        if (NinjaSlayerFinisherCinematic.TryPlayOwnedAction(creature, ActionDuration, out Task action))
        {
            await action;
            return;
        }

        var creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
        if (creatureNode == null) return;

        var originalPos = creatureNode.Position;
        var direction = creature.Side == CombatSide.Player ? 1f : -1f;

        var tween = creatureNode.CreateTween();

        tween.TweenMethod(
            Callable.From<float>(t =>
            {
                float xOffset;
                if (t < 0.5f)
                {
                    float easedT = FinisherActionTrajectory.SlowProgress(t * 2f);
                    xOffset = Mathf.Lerp(0f, LungeDistance, easedT);
                }
                else
                {
                    var fadeT = (1f - t) * 2f;
                    var easedT = fadeT * fadeT * (3f - 2f * fadeT);
                    xOffset = Mathf.Lerp(0f, LungeDistance, easedT);
                }

                creatureNode.Position = new Vector2(originalPos.X + xOffset * direction, originalPos.Y);
            }),
            0f,
            1f,
            AnimationDuration
        ).SetTrans(Tween.TransitionType.Linear);

        await Cmd.Wait(ActionDuration);
    }
}
