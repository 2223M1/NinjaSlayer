using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Nodes;

namespace NinjaSlayer.Code.ExternalAnimations;

public static class ByrdRiseAnimation
{
    private const float Duration = 0.3f;

    public static async Task Play(Creature creature, float riseDistance, float duration = Duration)
    {
        var creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
        if (creatureNode == null) return;

        var visuals = creatureNode.Visuals;
        if (visuals == null) return;

        var target = GetVerticalTarget(visuals, out bool usesAirborneAnchor);
        if (target == null) return;

        if (usesAirborneAnchor)
        {
            SoarVisualState.BeginAirborne(creature, riseDistance);
        }

        var originalPos = target.Position;

        var tween = creatureNode.CreateTween();
        tween.TweenProperty(target, "position:y",
                originalPos.Y - riseDistance, duration)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Quad);

        await creatureNode.ToSignal(tween, Tween.SignalName.Finished);
    }

    private static Node2D? GetVerticalTarget(NCreatureVisuals visuals, out bool usesAirborneAnchor)
    {
        var anchor = NinjaSlayerVisualRig.GetAirborneAnchor(visuals);
        usesAirborneAnchor = anchor != null;
        return anchor ?? visuals;
    }
}
