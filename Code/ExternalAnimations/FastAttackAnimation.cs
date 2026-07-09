using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.ExternalAnimations;

public static class FastAttackAnimation
{
    private const float AnimationDuration = 0.24f;
    private const float OriginalAnimationDuration = 0.4f;
    private const float LungeDistance = NinjaSlayerCombatVisuals.AttackLungeDistance * OriginalAnimationDuration / AnimationDuration;

    public static async Task Play(Creature creature, float waitTime, bool reverseDirection = false)
    {
        var creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
        if (creatureNode == null) return;

        var originalPos = creatureNode.Position;
        var direction = (creature.IsPlayer ? 1f : -1f) * (reverseDirection ? -1f : 1f);

        var tween = creatureNode.CreateTween();

        tween.TweenMethod(
            Callable.From<float>(timer =>
            {
                float xOffset;
                if (timer < 0f)
                {
                    xOffset = 0f;
                }
                else
                {
                    var t = timer / 1f * 2f;
                    var easedT = t * t * (3f - 2f * t);
                    xOffset = Mathf.Lerp(0f, LungeDistance, easedT);
                }

                creatureNode.Position = new Vector2(originalPos.X + xOffset * direction, originalPos.Y);
            }),
            AnimationDuration,
            0f,
            AnimationDuration
        ).SetTrans(Tween.TransitionType.Linear);

        await Cmd.Wait(waitTime);
    }
}
