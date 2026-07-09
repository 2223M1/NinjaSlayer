using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace NinjaSlayer.Code.ExternalAnimations;

public static class SlowAttackAnimation
{
    private const float AnimationDuration = 0.5f;
    private const float ActionDuration = 0.25f;
    private const float LungeDistance = 120f;

    public static async Task Play(Creature creature)
    {
        var creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
        if (creatureNode == null) return;

        var originalPos = creatureNode.Position;
        var direction = creature.IsPlayer ? 1f : -1f;

        var tween = creatureNode.CreateTween();

        tween.TweenMethod(
            Callable.From<float>(t =>
            {
                float xOffset;
                if (t < 0.5f)
                {
                    var easedT = Mathf.Pow(t * 2f, 10f);
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
