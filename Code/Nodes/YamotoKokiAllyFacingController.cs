using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Monsters;

namespace NinjaSlayer.Code.Nodes;

public partial class YamotoKokiAllyFacingController : Node
{
    private const string NodeName = nameof(YamotoKokiAllyFacingController);
    private NCombatRoom? _room;

    public static YamotoKokiAllyFacingController Ensure(NCombatRoom room)
    {
        YamotoKokiAllyFacingController? existing =
            room.GetNodeOrNull<YamotoKokiAllyFacingController>(NodeName);
        if (existing != null)
        {
            return existing;
        }

        var controller = new YamotoKokiAllyFacingController
        {
            Name = NodeName,
            _room = room
        };
        room.AddChild(controller);
        return controller;
    }

    internal static void SyncCurrentRoom()
    {
        if (NCombatRoom.Instance is not { } room
            || !room.CreatureNodes.Any(node =>
                node.Entity.Monster is YamotoKokiMonster or YukanoMonster))
        {
            return;
        }

        Ensure(room).SyncNow();
    }

    public override void _Process(double delta)
    {
        _ = delta;
        SyncNow();
    }

    internal void SyncNow()
    {
        if (_room == null || !GodotObject.IsInstanceValid(_room))
        {
            return;
        }

        var facingByOwner = new Dictionary<Creature, bool>(ReferenceEqualityComparer.Instance);
        foreach (NCreature companion in _room.CreatureNodes.Where(IsTrackedCompanion))
        {
            if (!companion.IsNodeReady()
                || companion.Entity.PetOwner?.Creature is not { } owner
                || _room.GetCreatureNode(owner) is not { } ownerNode
                || !ownerNode.IsNodeReady())
            {
                continue;
            }

            Node2D body = companion.Body;
            if (!facingByOwner.TryGetValue(owner, out bool faceLeft))
            {
                faceLeft = ResolveCompanionFacing(ownerNode);
                facingByOwner.Add(owner, faceLeft);
            }

            body.Scale = new Vector2(
                FacingScaleMath.WithFacing(body.Scale.X, faceLeft),
                body.Scale.Y);

            if (NinjaSlayerVisualRig.GetShadow(companion.Visuals) is { } shadow)
            {
                shadow.FlipH = faceLeft;
                if (companion.Entity.Monster is YukanoMonster)
                {
                    float centerX = Mathf.Abs(shadow.Position.X);
                    shadow.Position = new Vector2(faceLeft ? -centerX : centerX, shadow.Position.Y);
                }
            }
        }
    }

    private static bool ResolveCompanionFacing(NCreature ownerNode)
    {
        bool ownerFacesLeft = NinjaSlayerFacingState.ResolveFacingLeft(ownerNode);
        bool hasEnemyOnLeft = false;
        bool hasEnemyOnRight = false;
        foreach (Creature enemy in ownerNode.Entity.CombatState?.HittableEnemies ?? [])
        {
            hasEnemyOnLeft |= enemy.HasPower<BackAttackLeftPower>();
            hasEnemyOnRight |= enemy.HasPower<BackAttackRightPower>();
            if (hasEnemyOnLeft && hasEnemyOnRight)
            {
                break;
            }
        }

        return YamotoKokiFacingPolicy.ResolveCompanionFacing(
            ownerFacesLeft,
            hasEnemyOnLeft,
            hasEnemyOnRight);
    }

    private static bool IsTrackedCompanion(NCreature node) =>
        node.Entity.IsAlive
        && node.Entity.Monster is YamotoKokiMonster or YamotoKokiOrigamiMissile or YukanoMonster;
}
