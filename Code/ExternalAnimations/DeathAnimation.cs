using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Nodes;

namespace NinjaSlayer.Code.ExternalAnimations;

/// <summary>
/// NinjaSlayer death fall: CCW 90° around the bottom of the tornado-fist spin axis.
/// Tutorial has no equivalent; uses ExternalAnimation + FMOD via StartDeathAnim patch.
/// </summary>
public static class DeathAnimation
{
    public const float DurationSeconds = 0.45f;
    public const float TargetRotationDegrees = -90f;

    public static async Task Play(Creature creature)
    {
        var creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
        if (creatureNode == null)
        {
            return;
        }

        SoarSpinAnimation.ResetSpinVisual(creature);
        if (SoarVisualState.IsAirborne(creature))
        {
            SoarVisualState.ResetVisualsToGround(creature);
        }

        creatureNode.SetAnimationTrigger("Dead");

        var sprite = NinjaSlayerVisualRig.GetBodySprite(creatureNode.Visuals);
        if (sprite == null)
        {
            await creatureNode.ToSignal(creatureNode.GetTree().CreateTimer(DurationSeconds), SceneTreeTimer.SignalName.Timeout);
            return;
        }

        sprite.RotationDegrees = 0f;
        sprite.Offset = NinjaSlayerVisualRig.SpinAxisBottomOffset;

        var tween = creatureNode.CreateTween();
        tween.TweenProperty(sprite, "rotation_degrees", TargetRotationDegrees, DurationSeconds)
            .SetEase(Tween.EaseType.In)
            .SetTrans(Tween.TransitionType.Quad);

        await creatureNode.ToSignal(tween, Tween.SignalName.Finished);
    }
}
