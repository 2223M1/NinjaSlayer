using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;
using NinjaSlayer.Code.Nodes;

namespace NinjaSlayer.Code.ExternalAnimations;

public static class ByrdFallAnimation
{
    private const float Duration = 0.3f;
    public const float SquashDuration = 0.15f;

    public static async Task Play(
        Creature creature,
        float fallDistance,
        float duration = Duration,
        bool playImpact = true,
        Func<Task>? onImpact = null,
        ICinematicAnimationContext? cinematicContext = null)
    {
        var creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
        if (creatureNode == null) return;

        var visuals = creatureNode.Visuals;
        if (visuals == null) return;

        var target = GetVerticalTarget(visuals, out bool usesAirborneAnchor);
        if (target == null) return;

        var originalPos = target.Position;

        var tween = creatureNode.CreateTween();
        tween.TweenProperty(target, "position:y",
                originalPos.Y + fallDistance, duration)
            .SetEase(Tween.EaseType.In)
            .SetTrans(Tween.TransitionType.Quad);

        if (cinematicContext == null)
        {
            await creatureNode.ToSignal(tween, Tween.SignalName.Finished);
        }
        else
        {
            await cinematicContext.AwaitTween(creatureNode, tween);
        }

        if (usesAirborneAnchor)
        {
            SoarVisualState.ResetVisualsToGround(creature);
            HopAnimation.SyncBasePosition(creature, Vector2.Zero);
        }

        if (playImpact)
        {
            NGame.Instance?.ScreenShake(ShakeStrength.Medium, ShakeDuration.Short);
            SfxCmd.Play("event:/sfx/enemy/enemy_impact_enemy_size/enemy_impact_fur");
        }

        if (onImpact != null)
        {
            await onImpact();
        }
    }

    private static Node2D? GetVerticalTarget(NCreatureVisuals visuals, out bool usesAirborneAnchor)
    {
        var anchor = NinjaSlayerVisualRig.GetAirborneAnchor(visuals);
        usesAirborneAnchor = anchor != null;
        return anchor ?? visuals;
    }
}
