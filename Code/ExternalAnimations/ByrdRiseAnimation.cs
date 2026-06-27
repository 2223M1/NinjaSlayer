using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Nodes;

namespace NinjaSlayer.Code.ExternalAnimations;

public static class ByrdRiseAnimation
{
    private const float Duration = 0.3f;

    public static async Task Play(Creature creature, float riseDistance)
    {
        var creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
        if (creatureNode == null) return;

        var visuals = creatureNode.Visuals;
        if (visuals == null) return;

        var anchor = NinjaSlayerVisualRig.GetAirborneAnchor(visuals);
        if (anchor == null) return;

        SoarVisualState.BeginAirborne(creature, riseDistance);

        var originalPos = anchor.Position;

        var tween = creatureNode.CreateTween();
        tween.TweenProperty(anchor, "position:y",
                originalPos.Y - riseDistance, Duration)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Quad);

        await creatureNode.ToSignal(tween, Tween.SignalName.Finished);
    }
}
