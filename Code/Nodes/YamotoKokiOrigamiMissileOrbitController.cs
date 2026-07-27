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

    private static readonly NodePath DamageAmountPath = new("%DamageAmount");

    private readonly HashSet<Creature> _knownMissiles = [];
    private readonly Dictionary<NCreature, Vector2> _targets = [];
    private readonly Dictionary<NCreature, Tween> _layoutTweens = [];
    private readonly Dictionary<Player, int> _reservedMissileCounts = [];

    // LayoutNow runs every frame, so its working set is reused instead of rebuilt. The LINQ form
    // allocated a dictionary copy, two lists, a GroupBy lookup, several closures and a fresh
    // damage string on each of them.
    private readonly List<NCreature> _creatureNodeScratch = [];
    private readonly List<Player> _ownerOrder = [];
    private readonly Dictionary<Player, List<NCreature>> _missilesByOwner = [];
    private readonly Dictionary<NCreature, Vector2> _previousTargets = [];
    private readonly Dictionary<NCreature, MissileVisualState> _missileVisuals = [];
    private readonly List<NCreature> _staleTweenScratch = [];
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

        // The previous targets only drive the snap-to-tween comparison below.
        _previousTargets.Clear();
        if (snapNewMissiles)
        {
            foreach ((NCreature missile, Vector2 target) in _targets)
            {
                _previousTargets[missile] = target;
            }
        }

        _targets.Clear();
        _creatureNodeScratch.Clear();
        foreach (NCreature node in _room.CreatureNodes)
        {
            _creatureNodeScratch.Add(node);
        }

        GroupMissilesByOwner();
        foreach (Player owner in _ownerOrder)
        {
            List<NCreature> missiles = _missilesByOwner[owner];
            NCreature? yamotoKoki = FindYamotoKoki(owner);
            if (yamotoKoki == null)
            {
                continue;
            }

            int layoutCount = Math.Max(
                missiles.Count,
                _reservedMissileCounts.GetValueOrDefault(owner, missiles.Count));
            Vector2 center = yamotoKoki.Position
                + yamotoKoki.Visuals.VfxSpawnPosition.Position;
            Color ownerModulate = yamotoKoki.Visuals.Modulate;
            for (int i = 0; i < missiles.Count; i++)
            {
                NCreature missileNode = missiles[i];
                bool isNew = _knownMissiles.Add(missileNode.Entity);
                ApplyMissileVisuals(missileNode, ownerModulate);

                (float x, float y) = YamotoKokiOrbitMath.GetOffset(layoutCount, i);
                Vector2 target = center + new Vector2(x, y);
                _targets[missileNode] = target;
                if (snapNewMissiles && isNew)
                {
                    missileNode.Position = target;
                }
                else if (snapNewMissiles
                         && _previousTargets.TryGetValue(missileNode, out Vector2 previousTarget)
                         && previousTarget.DistanceSquaredTo(target) > 0.01f)
                {
                    StartLayoutTween(missileNode);
                }
            }
        }

        _staleTweenScratch.Clear();
        foreach (NCreature missile in _layoutTweens.Keys)
        {
            if (!_targets.ContainsKey(missile))
            {
                _staleTweenScratch.Add(missile);
            }
        }

        foreach (NCreature missile in _staleTweenScratch)
        {
            StopLayoutTween(missile);
        }

        _staleTweenScratch.Clear();
        PruneMissileVisuals();
        _knownMissiles.RemoveWhere(creature => creature.IsDead || creature.CombatState == null);
    }

    /// <summary>
    /// Reproduces the previous <c>Where(...).GroupBy(PetOwner)</c>: owners keep first-appearance
    /// order and each group keeps source order, because both decide the orbit slot a missile gets.
    /// </summary>
    private void GroupMissilesByOwner()
    {
        _ownerOrder.Clear();
        foreach (List<NCreature> missiles in _missilesByOwner.Values)
        {
            missiles.Clear();
        }

        foreach (NCreature node in _creatureNodeScratch)
        {
            if (node.Entity.Monster is not YamotoKokiOrigamiMissile { IsLaunching: false }
                || !node.Entity.IsAlive
                || node.Entity.PetOwner is not { } owner)
            {
                continue;
            }

            if (!_missilesByOwner.TryGetValue(owner, out List<NCreature>? missiles))
            {
                missiles = [];
                _missilesByOwner[owner] = missiles;
            }

            if (missiles.Count == 0)
            {
                _ownerOrder.Add(owner);
            }

            missiles.Add(node);
        }
    }

    private NCreature? FindYamotoKoki(Player owner)
    {
        foreach (NCreature node in _creatureNodeScratch)
        {
            if (node.Entity.Monster is YamotoKokiMonster
                && node.Entity.PetOwner == owner
                && node.Entity.IsAlive)
            {
                return node;
            }
        }

        return null;
    }

    private void ApplyMissileVisuals(NCreature missileNode, Color ownerModulate)
    {
        if (!_missileVisuals.TryGetValue(missileNode, out MissileVisualState state))
        {
            state = default;
        }

        Label? damageAmount = state.DamageAmount;
        if (damageAmount == null
            || !GodotObject.IsInstanceValid(damageAmount)
            || !missileNode.Visuals.IsAncestorOf(damageAmount))
        {
            damageAmount = missileNode.Visuals.GetNodeOrNull<Label>(DamageAmountPath);
            state = new MissileVisualState(damageAmount, LastDamage: null, DamageText: null);
        }

        if (missileNode.Entity.Monster is YamotoKokiOrigamiMissile missile
            && damageAmount != null
            && GodotObject.IsInstanceValid(damageAmount))
        {
            int damage = missile.GetExplodeDamage();
            if (state.LastDamage != damage || state.DamageText == null)
            {
                state = state with
                {
                    LastDamage = damage,
                    DamageText = damage.ToString(CultureInfo.InvariantCulture)
                };
            }

            // Reapply the cached value when another visual update overwrites the label, without
            // allocating the same damage string on every frame.
            if (damageAmount.Text != state.DamageText)
            {
                damageAmount.Text = state.DamageText;
            }
        }

        // Compared against the live value, not a cached one: the original assigned unconditionally
        // every frame, so anything the game tweens onto the missile must still be overwritten.
        if (missileNode.Visuals.Modulate != ownerModulate)
        {
            missileNode.Visuals.Modulate = ownerModulate;
        }

        _missileVisuals[missileNode] = state;
    }

    private void PruneMissileVisuals()
    {
        _staleTweenScratch.Clear();
        foreach (NCreature missile in _missileVisuals.Keys)
        {
            if (!_targets.ContainsKey(missile))
            {
                _staleTweenScratch.Add(missile);
            }
        }

        foreach (NCreature missile in _staleTweenScratch)
        {
            _missileVisuals.Remove(missile);
        }

        _staleTweenScratch.Clear();
    }

    private readonly record struct MissileVisualState(
        Label? DamageAmount,
        int? LastDamage,
        string? DamageText);

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
