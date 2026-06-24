using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace ActsFromThePast;
public static class SummonSlideInAnimation
{
    private const float SlideDuration = 0.5f;
    private const float StartOffsetX = 1200f;

    public static async Task Play(Creature creature)
    {
        var creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
        if (creatureNode == null) return;

        var finalPos = creatureNode.Position;
        var startPos = finalPos + new Vector2(StartOffsetX, 0f);

        // Set offscreen position BEFORE making visible
        creatureNode.Position = startPos;
        creatureNode.Visible = true;

        var tween = creatureNode.CreateTween();
        tween.TweenProperty(creatureNode, "position", finalPos, SlideDuration)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);

        await Cmd.Wait(SlideDuration);
    }
}