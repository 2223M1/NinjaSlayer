using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Code.Nodes;

[GlobalClass]
public partial class NinjaSlayerSpinPivot : Node2D
{
    private Sprite2D? sprite;
    private Creature? creature;
    private float normalScaleX = 0.33f;
    private Vector2? lastRawPosition;
    private Vector2 lastFinalPosition;

    public override void _Ready()
    {
        sprite = GetParent()?.GetNodeOrNull<Sprite2D>("%Visuals");
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

        bool usePivot = UseNormalPivot() && sprite.Scale.X < 0f;
        bool alreadyAdjusted = lastRawPosition.HasValue && sprite.Position.IsEqualApprox(lastFinalPosition);
        Vector2 rawPosition = alreadyAdjusted ? lastRawPosition!.Value : sprite.Position;

        Vector2 compensation = usePivot ? new Vector2(NinjaSlayerVisualRig.SpinPivotDeltaX * normalScaleX, 0f) : Vector2.Zero;

        sprite.Offset = usePivot ? new Vector2(-NinjaSlayerVisualRig.SpinPivotDeltaX, 0f) : Vector2.Zero;
        sprite.Position = rawPosition + compensation;
        lastRawPosition = rawPosition;
        lastFinalPosition = sprite.Position;
    }

    private bool UseNormalPivot()
    {
        return creature == null || (!creature.HasPower<NarakuPower>() && !creature.HasPower<OneBodyOneSoulPower>());
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
