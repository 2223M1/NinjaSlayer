using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Code.Nodes;

[GlobalClass]
public partial class NinjaSlayerSpinPivot : Node2D
{
    private Sprite2D? sprite;
    private Node2D? cinematicFocus;
    private Creature? creature;
    private float normalScaleX = 0.33f;
    private Vector2? lastRawPosition;
    private Vector2 lastFinalPosition;

    public override void _Ready()
    {
        sprite = GetParent()?.GetNodeOrNull<Sprite2D>("%Visuals");
        cinematicFocus = GetParent()?.GetNodeOrNull<Node2D>(NinjaSlayerVisualRig.CinematicFocusName);
        creature = FindCreature();
        if (sprite != null)
        {
            normalScaleX = Mathf.Abs(sprite.Scale.X);
        }
    }

    public override void _Process(double delta)
    {
        if (sprite == null || !GodotObject.IsInstanceValid(sprite))
        {
            return;
        }

        creature ??= FindCreature();
        if (creature?.IsDead == true)
        {
            return;
        }

        bool alreadyAdjusted = lastRawPosition.HasValue && sprite.Position.IsEqualApprox(lastFinalPosition);
        Vector2 rawPosition = alreadyAdjusted ? lastRawPosition!.Value : sprite.Position;
        bool usePivot = UseNormalPivot()
            && sprite.Scale.X < 0f
            && !HasAuthoredFixedPivot(rawPosition);

        Vector2 compensation = usePivot ? new Vector2(NinjaSlayerVisualRig.SpinPivotDeltaX * normalScaleX, 0f) : Vector2.Zero;

        sprite.Offset = usePivot ? new Vector2(-NinjaSlayerVisualRig.SpinPivotDeltaX, 0f) : Vector2.Zero;
        sprite.Position = rawPosition + compensation;
        lastRawPosition = rawPosition;
        lastFinalPosition = sprite.Position;
    }

    private bool HasAuthoredFixedPivot(Vector2 rawPosition)
    {
        if (cinematicFocus == null || !GodotObject.IsInstanceValid(cinematicFocus))
        {
            return false;
        }

        float authoredAxisX = rawPosition.X
            + NinjaSlayerVisualRig.SpinPivotDeltaX * sprite!.Scale.X;
        return Mathf.IsEqualApprox(authoredAxisX, cinematicFocus.Position.X);
    }

    private bool UseNormalPivot()
    {
        return creature == null
            || (!SoarSpinAnimation.IsVerticalSpinActive(creature)
                && !NinjaSlayerFormState.IsFullyReleasedNaraku(creature)
                && !creature.HasPower<OneBodyOneSoulPower>());
    }

    private Creature? FindCreature()
    {
        for (Node? node = this; node != null; node = node.GetParent())
        {
            if (node is NCreature creatureNode)
            {
                return creatureNode.Entity;
            }
        }

        return null;
    }
}
