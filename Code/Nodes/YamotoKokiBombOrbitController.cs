using Godot;
using System.Globalization;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Monsters;

namespace NinjaSlayer.Code.Nodes;

public partial class YamotoKokiBombOrbitController : Node
{
    private const string NodeName = "YamotoKokiBombOrbitController";
    private const float BombVisualScale = 0.5f;
    private const float FollowSpeed = 9f;

    private readonly HashSet<Creature> _knownBombs = [];
    private readonly Dictionary<NCreature, Vector2> _targets = [];
    private NCombatRoom? _room;

    public static YamotoKokiBombOrbitController Ensure(NCombatRoom room)
    {
        YamotoKokiBombOrbitController? existing =
            room.GetNodeOrNull<YamotoKokiBombOrbitController>(NodeName);
        if (existing != null)
        {
            return existing;
        }

        YamotoKokiBombOrbitController controller = new()
        {
            Name = NodeName,
            _room = room
        };
        room.AddChild(controller);
        return controller;
    }

    public override void _Process(double delta)
    {
        LayoutNow(snapNewBombs: false);
        float weight = 1f - Mathf.Exp(-FollowSpeed * (float)delta);
        foreach ((NCreature bomb, Vector2 target) in _targets)
        {
            if (GodotObject.IsInstanceValid(bomb))
            {
                bomb.Position = bomb.Position.Lerp(target, weight);
            }
        }
    }

    public void LayoutNow(bool snapNewBombs)
    {
        if (_room == null || !GodotObject.IsInstanceValid(_room))
        {
            return;
        }

        _targets.Clear();
        List<NCreature> nodes = _room.CreatureNodes.ToList();
        foreach (IGrouping<MegaCrit.Sts2.Core.Entities.Players.Player?, NCreature> group in nodes
                     .Where(node => node.Entity.Monster is YamotoKokiGasBomb && node.Entity.IsAlive)
                     .GroupBy(node => node.Entity.PetOwner))
        {
            if (group.Key == null)
            {
                continue;
            }

            NCreature? yamotoKoki = nodes.FirstOrDefault(node =>
                node.Entity.Monster is YamotoKokiMonster
                && node.Entity.PetOwner == group.Key
                && node.Entity.IsAlive);
            if (yamotoKoki == null)
            {
                continue;
            }

            List<NCreature> bombs = group.ToList();
            Vector2 center = yamotoKoki.Position
                + yamotoKoki.Visuals.VfxSpawnPosition.Position;
            for (int i = 0; i < bombs.Count; i++)
            {
                NCreature bomb = bombs[i];
                bool isNew = _knownBombs.Add(bomb.Entity);
                if (!Mathf.IsEqualApprox(bomb.Visuals.DefaultScale, BombVisualScale))
                {
                    bomb.SetScaleAndHue(BombVisualScale, 0f);
                }

                if (bomb.Entity.Monster is YamotoKokiGasBomb missile)
                {
                    Label? damageAmount = bomb.Visuals.GetNodeOrNull<Label>("%DamageAmount");
                    if (damageAmount != null)
                    {
                        damageAmount.Text = missile.GetExplodeDamage()
                            .ToString(CultureInfo.InvariantCulture);
                    }
                }

                (float x, float y) = YamotoKokiOrbitMath.GetOffset(bombs.Count, i);
                Vector2 target = center + new Vector2(x, y);
                _targets[bomb] = target;
                bomb.Visuals.Modulate = yamotoKoki.Visuals.Modulate;
                if (snapNewBombs && isNew)
                {
                    bomb.Position = target;
                }
            }
        }

        _knownBombs.RemoveWhere(creature => creature.IsDead || creature.CombatState == null);
    }
}
