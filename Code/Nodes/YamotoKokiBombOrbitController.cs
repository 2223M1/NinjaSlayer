using Godot;
using System.Globalization;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Monsters;

namespace NinjaSlayer.Code.Nodes;

public partial class YamotoKokiBombOrbitController : Node
{
    private const string NodeName = "YamotoKokiBombOrbitController";
    private const float FollowSpeed = 9f;
    private const float LayoutTweenSeconds = 0.45f;

    private readonly HashSet<Creature> _knownBombs = [];
    private readonly Dictionary<NCreature, Vector2> _targets = [];
    private readonly Dictionary<NCreature, Tween> _layoutTweens = [];
    private readonly Dictionary<Player, int> _reservedBombCounts = [];
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

    public void BeginSpawnBatch(Player owner, int incomingBombCount)
    {
        if (_room == null || !GodotObject.IsInstanceValid(_room) || incomingBombCount <= 0)
        {
            return;
        }

        int existingBombCount = _room.CreatureNodes.Count(node =>
            node.Entity.Monster is YamotoKokiGasBomb { IsLaunching: false }
            && node.Entity.PetOwner == owner
            && node.Entity.IsAlive);
        _reservedBombCounts[owner] = existingBombCount + incomingBombCount;
        LayoutNow(snapNewBombs: true);
    }

    public void EndSpawnBatch(Player owner)
    {
        if (_reservedBombCounts.Remove(owner))
        {
            LayoutNow(snapNewBombs: true);
        }
    }

    public override void _Process(double delta)
    {
        LayoutNow(snapNewBombs: false);
        float weight = 1f - Mathf.Exp(-FollowSpeed * (float)delta);
        foreach ((NCreature bomb, Vector2 target) in _targets)
        {
            if (GodotObject.IsInstanceValid(bomb) && !_layoutTweens.ContainsKey(bomb))
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

        Dictionary<NCreature, Vector2> previousTargets = new(_targets);
        _targets.Clear();
        List<NCreature> nodes = _room.CreatureNodes.ToList();
        foreach (IGrouping<MegaCrit.Sts2.Core.Entities.Players.Player?, NCreature> group in nodes
                     .Where(node => node.Entity.Monster is YamotoKokiGasBomb { IsLaunching: false }
                         && node.Entity.IsAlive)
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
            int layoutCount = Math.Max(
                bombs.Count,
                _reservedBombCounts.GetValueOrDefault(group.Key, bombs.Count));
            Vector2 center = yamotoKoki.Position
                + yamotoKoki.Visuals.VfxSpawnPosition.Position;
            for (int i = 0; i < bombs.Count; i++)
            {
                NCreature bomb = bombs[i];
                bool isNew = _knownBombs.Add(bomb.Entity);
                if (bomb.Entity.Monster is YamotoKokiGasBomb missile)
                {
                    Label? damageAmount = bomb.Visuals.GetNodeOrNull<Label>("%DamageAmount");
                    if (damageAmount != null)
                    {
                        damageAmount.Text = missile.GetExplodeDamage()
                            .ToString(CultureInfo.InvariantCulture);
                    }
                }

                (float x, float y) = YamotoKokiOrbitMath.GetOffset(layoutCount, i);
                Vector2 target = center + new Vector2(x, y);
                _targets[bomb] = target;
                bomb.Visuals.Modulate = yamotoKoki.Visuals.Modulate;
                if (snapNewBombs && isNew)
                {
                    bomb.Position = target;
                }
                else if (snapNewBombs
                         && previousTargets.TryGetValue(bomb, out Vector2 previousTarget)
                         && previousTarget.DistanceSquaredTo(target) > 0.01f)
                {
                    StartLayoutTween(bomb);
                }
            }
        }

        foreach (NCreature bomb in _layoutTweens.Keys
                     .Where(bomb => !_targets.ContainsKey(bomb))
                     .ToList())
        {
            StopLayoutTween(bomb);
        }

        _knownBombs.RemoveWhere(creature => creature.IsDead || creature.CombatState == null);
    }

    private void StartLayoutTween(NCreature bomb)
    {
        StopLayoutTween(bomb);
        Vector2 start = bomb.Position;
        Tween tween = CreateTween();
        _layoutTweens[bomb] = tween;
        tween.TweenMethod(
                Callable.From<float>(progress =>
                {
                    if (GodotObject.IsInstanceValid(bomb)
                        && _targets.TryGetValue(bomb, out Vector2 target))
                    {
                        bomb.Position = start.Lerp(target, progress);
                    }
                }),
                0f,
                1f,
                LayoutTweenSeconds)
            .SetEase(Tween.EaseType.InOut)
            .SetTrans(Tween.TransitionType.Sine);
        tween.TweenCallback(Callable.From(() => CompleteLayoutTween(bomb, tween)));
    }

    private void CompleteLayoutTween(NCreature bomb, Tween tween)
    {
        if (!_layoutTweens.TryGetValue(bomb, out Tween? active)
            || !ReferenceEquals(active, tween))
        {
            return;
        }

        _layoutTweens.Remove(bomb);
        if (GodotObject.IsInstanceValid(bomb)
            && _targets.TryGetValue(bomb, out Vector2 target))
        {
            bomb.Position = target;
        }
    }

    private void StopLayoutTween(NCreature bomb)
    {
        if (_layoutTweens.Remove(bomb, out Tween? tween))
        {
            tween.Kill();
        }
    }
}
