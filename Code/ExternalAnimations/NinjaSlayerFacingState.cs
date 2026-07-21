using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class NinjaSlayerFacingState
{
    public static async Task SyncAfterBeforeCardPlayed(Task original, CardPlay cardPlay)
    {
        await original;
        if (cardPlay.Card.Type == CardType.Attack && cardPlay.Target != null)
        {
            SyncForTarget(cardPlay.Card.Owner.Creature, cardPlay.Target);
        }
    }

    public static async Task TransferSurroundedFacing(
        Task original,
        SurroundedPower power,
        Creature? creature,
        float bodyScaleX,
        bool restoreBodyScale)
    {
        try
        {
            await original;
        }
        finally
        {
            if (creature != null && restoreBodyScale)
            {
                RestoreBodyScaleX(creature, bodyScaleX);
            }

            if (creature != null && power.Owner == creature)
            {
                SyncFromSurroundedPower(creature, power);
            }
        }
    }

    public static void SyncForTarget(Creature creature, Creature target)
    {
        if (creature.Player?.Character is not INinjaSlayerCharacter)
        {
            return;
        }

        if (creature.GetPower<SurroundedPower>() is { } surrounded)
        {
            SyncFromSurroundedPower(creature, surrounded);
            return;
        }

        NCombatRoom? room = NCombatRoom.Instance;
        NCreature? creatureNode = room?.GetCreatureNode(creature);
        NCreature? targetNode = room?.GetCreatureNode(target);
        if (creatureNode == null || targetNode == null)
        {
            return;
        }

        Apply(creatureNode, targetNode.GlobalPosition.X < creatureNode.GlobalPosition.X);
    }

    internal static void SetFacing(NCreature creatureNode, bool faceLeft)
    {
        if (creatureNode.Entity.Player?.Character is INinjaSlayerCharacter)
        {
            Apply(creatureNode, faceLeft);
        }
    }

    public static (Creature? Creature, float BodyScaleX, bool RestoreBodyScale) CaptureSurroundedBody(
        SurroundedPower power)
    {
        Creature creature = power.Owner;
        if (creature.Player?.Character is not INinjaSlayerCharacter
            || NCombatRoom.Instance?.GetCreatureNode(creature)?.Body is not { } body)
        {
            return (null, 0f, false);
        }

        return (creature, body.Scale.X, true);
    }

    private static void SyncFromSurroundedPower(Creature creature, SurroundedPower power)
    {
        NCreature? creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
        if (creatureNode != null)
        {
            Apply(creatureNode, power.Facing == SurroundedPower.Direction.Left);
        }
    }

    private static void Apply(NCreature creatureNode, bool faceLeft)
    {
        Node2D? anchor = NinjaSlayerVisualRig.GetAirborneAnchor(creatureNode.Visuals);
        if (anchor == null)
        {
            return;
        }

        float magnitude = Mathf.Abs(anchor.Scale.X);
        if (magnitude <= 0.001f)
        {
            magnitude = 1f;
        }

        anchor.Scale = new Vector2(faceLeft ? -magnitude : magnitude, anchor.Scale.Y);
    }

    private static void RestoreBodyScaleX(Creature creature, float scaleX)
    {
        Node2D? body = NCombatRoom.Instance?.GetCreatureNode(creature)?.Body;
        if (body != null && GodotObject.IsInstanceValid(body))
        {
            body.Scale = new Vector2(scaleX, body.Scale.Y);
        }
    }
}
