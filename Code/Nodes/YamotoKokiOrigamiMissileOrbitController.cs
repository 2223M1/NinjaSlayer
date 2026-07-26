using Godot;
using System.Globalization;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Monsters;

namespace NinjaSlayer.Code.Nodes;

public partial class YamotoKokiOrigamiMissileOrbitController : Node
{
    private const string NodeName = "YamotoKokiOrigamiMissileOrbitController";
    private const float FollowSpeed = 9f;
    private const float LayoutTweenSeconds = 0.45f;

    private readonly HashSet<Creature> _knownMissiles = [];
    private readonly Dictionary<NCreature, Vector2> _targets = [];
    private readonly Dictionary<NCreature, Tween> _layoutTweens = [];
    private readonly Dictionary<Player, int> _reservedMissileCounts = [];
    private NCombatRoom? _room;

    public static YamotoKokiOrigamiMissileOrbitController Ensure(NCombatRoom room)
    {
        YamotoKokiOrigamiMissileOrbitController? existing =
            room.GetNodeOrNull<YamotoKokiOrigamiMissileOrbitController>(NodeName);
        if (existing != null)
        {
            return existing;
        }

        YamotoKokiOrigamiMissileOrbitController controller = new()
        {
            Name = NodeName,
            _room = room
        };
        room.AddChild(controller);
        return controller;
    }

    public void BeginSpawnBatch(Player owner, int incomingMissileCount)
    {
        if (_room == null || !GodotObject.IsInstanceValid(_room) || incomingMissileCount <= 0)
        {
            return;
        }

        int existingMissileCount = _room.CreatureNodes.Count(node =>
            node.Entity.Monster is YamotoKokiOrigamiMissile { IsLaunching: false }
            && node.Entity.PetOwner == owner
            && node.Entity.IsAlive);
        _reservedMissileCounts[owner] = existingMissileCount + incomingMissileCount;
        LayoutNow(snapNewMissiles: true);
    }

    public void EndSpawnBatch(Player owner)
    {
        if (_reservedMissileCounts.Remove(owner))
        {
            LayoutNow(snapNewMissiles: true);
        }
    }

    public override void _Process(double delta)
    {
        LayoutNow(snapNewMissiles: false);
        float weight = 1f - Mathf.Exp(-FollowSpeed * (float)delta);
        foreach ((NCreature missile, Vector2 target) in _targets)
        {
            if (GodotObject.IsInstanceValid(missile) && !_layoutTweens.ContainsKey(missile))
            {
                missile.Position = missile.Position.Lerp(target, weight);
            }
        }
    }

    public void LayoutNow(bool snapNewMissiles)
    {
        if (_room == null || !GodotObject.IsInstanceValid(_room))
        {
            return;
        }

        Dictionary<NCreature, Vector2> previousTargets = new(_targets);
        _targets.Clear();
        List<NCreature> nodes = _room.CreatureNodes.ToList();
        foreach (IGrouping<MegaCrit.Sts2.Core.Entities.Players.Player?, NCreature> group in nodes
                     .Where(node => node.Entity.Monster is YamotoKokiOrigamiMissile { IsLaunching: false }
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

            List<NCreature> missiles = group.ToList();
            int layoutCount = Math.Max(
                missiles.Count,
                _reservedMissileCounts.GetValueOrDefault(group.Key, missiles.Count));
            Vector2 center = yamotoKoki.Position
                + yamotoKoki.Visuals.VfxSpawnPosition.Position;
            for (int i = 0; i < missiles.Count; i++)
            {
                NCreature missileNode = missiles[i];
                bool isNew = _knownMissiles.Add(missileNode.Entity);
                if (missileNode.Entity.Monster is YamotoKokiOrigamiMissile missile)
                {
                    Label? damageAmount = missileNode.Visuals.GetNodeOrNull<Label>("%DamageAmount");
                    if (damageAmount != null)
                    {
                        damageAmount.Text = missile.GetExplodeDamage()
                            .ToString(CultureInfo.InvariantCulture);
                    }
                }

                (float x, float y) = YamotoKokiOrbitMath.GetOffset(layoutCount, i);
                Vector2 target = center + new Vector2(x, y);
                _targets[missileNode] = target;
                missileNode.Visuals.Modulate = yamotoKoki.Visuals.Modulate;
                if (snapNewMissiles && isNew)
                {
                    missileNode.Position = target;
                }
                else if (snapNewMissiles
                         && previousTargets.TryGetValue(missileNode, out Vector2 previousTarget)
                         && previousTarget.DistanceSquaredTo(target) > 0.01f)
                {
                    StartLayoutTween(missileNode);
                }
            }
        }

        foreach (NCreature missile in _layoutTweens.Keys
                     .Where(missile => !_targets.ContainsKey(missile))
                     .ToList())
        {
            StopLayoutTween(missile);
        }

        _knownMissiles.RemoveWhere(creature => creature.IsDead || creature.CombatState == null);
    }

    private void StartLayoutTween(NCreature missile)
    {
        StopLayoutTween(missile);
        Vector2 start = missile.Position;
        Tween tween = CreateTween();
        _layoutTweens[missile] = tween;
        tween.TweenMethod(
                Callable.From<float>(progress =>
                {
                    if (GodotObject.IsInstanceValid(missile)
                        && _targets.TryGetValue(missile, out Vector2 target))
                    {
                        missile.Position = start.Lerp(target, progress);
                    }
                }),
                0f,
                1f,
                LayoutTweenSeconds)
            .SetEase(Tween.EaseType.InOut)
            .SetTrans(Tween.TransitionType.Sine);
        tween.TweenCallback(Callable.From(() => CompleteLayoutTween(missile, tween)));
    }

    private void CompleteLayoutTween(NCreature missile, Tween tween)
    {
        if (!_layoutTweens.TryGetValue(missile, out Tween? active)
            || !ReferenceEquals(active, tween))
        {
            return;
        }

        _layoutTweens.Remove(missile);
        if (GodotObject.IsInstanceValid(missile)
            && _targets.TryGetValue(missile, out Vector2 target))
        {
            missile.Position = target;
        }
    }

    private void StopLayoutTween(NCreature missile)
    {
        if (_layoutTweens.Remove(missile, out Tween? tween))
        {
            tween.Kill();
        }
    }
}
