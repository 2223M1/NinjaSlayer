using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;
using NinjaSlayer.Code.Nodes;

namespace NinjaSlayer.Code.ExternalAnimations;

public static class ByrdFallAnimation
{
    private const float Duration = 0.3f;
    public const float SquashDuration = 0.15f;

    public static async Task Play(Creature creature, float fallDistance, float duration = Duration)
    {
        var creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
        if (creatureNode == null) return;

        var visuals = creatureNode.Visuals;
        if (visuals == null) return;

        var anchor = NinjaSlayerVisualRig.GetAirborneAnchor(visuals);
        if (anchor == null) return;

        var originalPos = anchor.Position;

        var tween = creatureNode.CreateTween();
        tween.TweenProperty(anchor, "position:y",
                originalPos.Y + fallDistance, duration)
            .SetEase(Tween.EaseType.In)
            .SetTrans(Tween.TransitionType.Quad);

        await creatureNode.ToSignal(tween, Tween.SignalName.Finished);

        SoarVisualState.ResetVisualsToGround(creature);
        HopAnimation.SyncBasePosition(creature, Vector2.Zero);

        NGame.Instance?.ScreenShake(ShakeStrength.Medium, ShakeDuration.Short);

        SfxCmd.Play("event:/sfx/enemy/enemy_impact_enemy_size/enemy_impact_fur");
    }
}
