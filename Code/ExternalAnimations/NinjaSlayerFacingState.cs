using Godot;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class NinjaSlayerFacingState
{
    private static readonly ConditionalWeakTable<Creature, FacingSnapshot> PersistentFacing = new();

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

            YamotoKokiAllyFacingController.SyncCurrentRoom();
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

    internal static bool ResolveFacingLeft(NCreature creatureNode)
    {
        Node2D? posedAnchor = NinjaSlayerVisualRig.GetAirborneAnchor(creatureNode.Visuals);
        if (posedAnchor?.GetNodeOrNull<NinjaSlayerAimPose>("AimPose") is { } pose
            && (pose.IsAiming || pose.HasFacingPreview))
            return posedAnchor.Scale.X < 0f;
        return ResolveCommittedFacingLeft(creatureNode);
    }

    internal static bool ResolveCommittedFacingLeft(NCreature creatureNode)
    {
        Creature creature = creatureNode.Entity;
        if (creature.Player?.Character is not INinjaSlayerCharacter)
        {
            return FacingScaleMath.IsFacingLeft(creatureNode.Body.Scale.X);
        }

        if (creature.GetPower<SurroundedPower>() is { } surrounded)
        {
            bool powerLeft = surrounded.Facing == SurroundedPower.Direction.Left;
            CommitFacing(creatureNode, powerLeft);
            return powerLeft;
        }

        if (PersistentFacing.TryGetValue(creature, out FacingSnapshot? snapshot))
        {
            return snapshot.FaceLeft;
        }

        Node2D? anchor = NinjaSlayerVisualRig.GetAirborneAnchor(creatureNode.Visuals);
        bool left = anchor != null && FacingScaleMath.IsFacingLeft(anchor.Scale.X);
        PersistentFacing.GetValue(creature, static _ => new FacingSnapshot()).FaceLeft = left;
        return left;
    }

    internal static void SetPreviewFacing(NCreature creatureNode, bool faceLeft)
    {
        _ = ResolveCommittedFacingLeft(creatureNode);
        ApplyVisual(creatureNode, faceLeft);
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

    internal static void CommitFacing(NCreature creatureNode, bool faceLeft) =>
        PersistentFacing.GetValue(creatureNode.Entity, static _ => new FacingSnapshot()).FaceLeft = faceLeft;

    private static void Apply(NCreature creatureNode, bool faceLeft)
    {
        CommitFacing(creatureNode, faceLeft);
        if (NinjaSlayerAimPose.Get(creatureNode.Entity)?.HasFacingPreview != true)
            ApplyVisual(creatureNode, faceLeft);
    }

    private static void ApplyVisual(NCreature creatureNode, bool faceLeft)
    {
        Node2D? anchor = NinjaSlayerVisualRig.GetAirborneAnchor(creatureNode.Visuals);
        if (anchor == null)
        {
            return;
        }

        anchor.Scale = new Vector2(
            FacingScaleMath.WithFacing(anchor.Scale.X, faceLeft),
            anchor.Scale.Y);
        creatureNode.Visuals
            .GetNodeOrNull<NinjaSlayerShadowController>(NinjaSlayerVisualRig.ShadowControllerNodeName)
            ?.SetMirrored(faceLeft);
        NarakuVisualOverlay.Sync(creatureNode.Entity);
    }

    private static void RestoreBodyScaleX(Creature creature, float scaleX)
    {
        Node2D? body = NCombatRoom.Instance?.GetCreatureNode(creature)?.Body;
        if (body != null && GodotObject.IsInstanceValid(body))
        {
            body.Scale = new Vector2(scaleX, body.Scale.Y);
        }
    }

    private sealed class FacingSnapshot
    {
        public bool FaceLeft { get; set; }
    }
}
