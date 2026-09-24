using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Unlocks;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Monsters;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static void VerifyNativeCompanionLayout()
    {
        _ = ModelDb.Monster<SawatariMonster>().AssetPaths.ToArray();
        using var combat = new OrbCombat();
        var players = new List<Player> { combat.Player };
        for (ulong id = 2; id <= 4; id++)
        {
            var player = id == 4 ? Player.CreateForNewRun<Necrobinder>(UnlockState.all, id)
                : Player.CreateForNewRun<Ironclad>(UnlockState.all, id);
            combat.State.AddPlayer(player);
            player.ResetCombatState();
            players.Add(player);
        }
        Creature Pet(MonsterModel monster)
        {
            var pet = new Creature(monster.ToMutable(), CombatSide.Player, null)
                { CombatState = combat.State, PetOwner = players[3] };
            AccessTools.Method(typeof(CombatState), "AttachCreature").Invoke(combat.State, [pet]);
            combat.State.AddCreature(pet);
            pet.SetMaxHpInternal(100);
            pet.SetCurrentHpInternal(100);
            return pet;
        }
        Creature[] pets = [Pet(ModelDb.Monster<YamotoKokiMonster>()),
            Pet(ModelDb.Monster<SawatariMonster>()), Pet(ModelDb.Monster<YukanoMonster>())];
        Creature missile = Pet(ModelDb.Monster<YamotoKokiOrigamiMissile>());
        Creature osty = Pet(ModelDb.Monster<Osty>());
        var parent = new Control();
        _hurtRoom = new NCombatRoom();
        var roomNodes = (List<NCreature>)AccessTools.Field(typeof(NCombatRoom), "_creatureNodes").GetValue(_hurtRoom)!;
        NCreature Node(Creature creature, float width)
        {
            var node = new NCreature();
            var visuals = new NCreatureVisuals();
            var bounds = new Control { Size = new Vector2(width, 200) };
            visuals.AddChild(bounds);
            node.AddChild(visuals);
            AccessTools.Property(typeof(NCreatureVisuals), "Bounds").SetValue(visuals, bounds);
            AccessTools.Property(typeof(NCreature), "Visuals").SetValue(node, visuals);
            AccessTools.Property(typeof(NCreature), "Entity").SetValue(node, creature);
            var hitbox = new Control { Size = bounds.Size };
            node.AddChild(hitbox);
            AccessTools.Property(typeof(NCreature), "Hitbox").SetValue(node, hitbox);
            roomNodes.Add(node);
            parent.AddChild(node);
            return node;
        }
        var nodes = players.Select((p, i) => Node(p.Creature, 180 + i * 25)).ToList();
        nodes.AddRange(pets.Select((pet, i) => Node(pet, 145 + i * 30)));
        NCreature missileNode = Node(missile, 35);
        missileNode.Position = new Vector2(812, 914);
        nodes.Add(missileNode);
        NCreature ostyNode = Node(osty, 80);
        nodes.Add(ostyNode);
        ulong? oldLocal = LocalContext.NetId;
        var patch = new Harmony("NinjaSlayer.CompanionLayoutContract");
        try
        {
            patch.Patch(AccessTools.PropertyGetter(typeof(NCombatRoom), "Instance"),
                prefix: new HarmonyMethod(typeof(OrbContractRunner), nameof(ResolveHurtRoom)));
            patch.Patch(AccessTools.Method(typeof(NCombatRoom), nameof(NCombatRoom.PositionPlayersAndPets)),
                prefix: new HarmonyMethod(typeof(YamotoKokiAllyLayoutPatch), nameof(YamotoKokiAllyLayoutPatch.Prefix)),
                transpiler: new HarmonyMethod(typeof(YamotoKokiAllyLayoutPatch), nameof(YamotoKokiAllyLayoutPatch.Transpiler)));
            foreach (Player local in players)
            foreach (bool centered in new[] { false, true })
            foreach (float scale in new[] { 0.8f, 1.2f })
            {
                LocalContext.NetId = local.NetId;
                List<NCreature> order = [nodes.Single(n => n.Entity == local.Creature),
                    ..pets.Select(p => nodes.Single(n => n.Entity == p)),
                    ..nodes.Where(n => n.Entity.IsPlayer && n.Entity != local.Creature)];
                var reference = order.Select((node, i) => Node(
                    i == 0 ? local.Creature : Player.CreateForNewRun<Ironclad>(UnlockState.all, (ulong)(900 + i)).Creature,
                    node.Visuals.Bounds.Size.X)).ToList();
                var referenceOsty = new Creature(ModelDb.Monster<Osty>().ToMutable(), CombatSide.Player, null)
                    { PetOwner = reference[order.FindIndex(n => n.Entity == players[3].Creature)].Entity.Player };
                referenceOsty.SetMaxHpInternal(osty.MaxHp);
                NCreature referenceOstyNode = Node(referenceOsty, 80);
                reference.Add(referenceOstyNode);
                // A native all-player arrangement is the geometry oracle, with the same bounds.
                NCombatRoom.PositionPlayersAndPets(reference, scale, centered);
                NCombatRoom.PositionPlayersAndPets(nodes, scale, centered);
                for (int i = 0; i < order.Count; i++)
                {
                    Require(order[i].Position.IsEqualApprox(reference[i].Position),
                        $"Companion native slot {i + 1} differs on local player {local.NetId}.");
                    Require(order[i].Visuals.Modulate.IsEqualApprox(reference[i].Visuals.Modulate),
                        "Companion layout left stale row tint.");
                }
                Require(ostyNode.Position.IsEqualApprox(referenceOstyNode.Position),
                    "Companion grouping changed the native local/remote Osty position.");
                foreach (NCreature node in reference) { roomNodes.Remove(node); node.Free(); }
                Require(missileNode.Position == new Vector2(812, 914), "Native slot layout moved an orbiting missile.");
                Require(combat.State.Players.Count == 4 && pets.All(p => p.IsPet && !p.IsPlayer && p.PetOwner == players[3]),
                    "Layout changed real player identity or pet ownership.");
            }
        }
        finally
        {
            patch.UnpatchAll(patch.Id);
            LocalContext.NetId = oldLocal;
            parent.Free();
            _hurtRoom.Free();
            _hurtRoom = null;
            foreach (Player player in players.Skip(1)) player.PlayerCombatState!.AfterCombatEnd();
        }
        GD.Print("PASS native companion slots: four client perspectives, 4 players + 3 pets, scale/centering, tint, Osty and orbit isolation");
    }

    private static void VerifyFriendlyIntentMaterials()
    {
        using var combat = new OrbCombat();
        foreach (MonsterModel model in new MonsterModel[] {
            ModelDb.Monster<YamotoKokiMonster>(), ModelDb.Monster<SawatariMonster>(), ModelDb.Monster<YukanoMonster>() })
        {
            var owner = new Creature(model.ToMutable(), CombatSide.Player, null)
                { CombatState = combat.State, PetOwner = combat.Player };
            owner.SetMaxHpInternal(100);
            owner.SetCurrentHpInternal(100);
            if (owner.Monster is YamotoKokiMonster koki)
            {
                koki.SetUpForCombat();
                var summon = (MoveState)koki.MoveStateMachine!.States[YamotoKokiMonster.SummonMissileMoveId];
                var slash = (MoveState)koki.MoveStateMachine.States[YamotoKokiMonster.IaiSlashMoveId];
                Require(summon.Intents.Single().GetAnimation([], owner) == new SummonIntent().GetAnimation([], owner)
                    && slash.Intents.Single().GetAnimation([], owner)
                        == new SingleAttackIntent(koki.GetIaiSlashDamage()).GetAnimation([], owner),
                    "Ally hover text changed the native intent animation key.");
            }
            var node = new NIntent();
            var icon = new Sprite2D { Name = "Intent", Material = new CanvasItemMaterial() };
            var particles = new CpuParticles2D { Name = "IntentParticle", Material = new CanvasItemMaterial() };
            node.AddChild(icon); icon.Owner = node; icon.UniqueNameInOwner = true;
            node.AddChild(particles); particles.Owner = node; particles.UniqueNameInOwner = true;
            Material originalIcon = icon.Material, originalParticles = particles.Material;
            try
            {
                foreach (AbstractIntent attack in new AbstractIntent[] { new SingleAttackIntent(8), new MultiAttackIntent(4, 3) })
                {
                    YamotoKokiIntentUpdatePatch.Postfix(node, attack, owner);
                    Require(icon.Material is ShaderMaterial && ReferenceEquals(icon.Material, particles.Material),
                        "Friendly native attack icon/particles did not share the yellow material.");
                    foreach (AbstractIntent neutral in new AbstractIntent[] { new SummonIntent(), new HealIntent(), new DefendIntent() })
                    {
                        YamotoKokiIntentUpdatePatch.Postfix(node, neutral, owner);
                        Require(icon.Material == originalIcon && particles.Material == originalParticles,
                            "Nonattack intent did not restore its original materials.");
                        YamotoKokiIntentUpdatePatch.Postfix(node, attack, owner);
                    }
                    var enemy = new Creature(model.ToMutable(), CombatSide.Enemy, null) { CombatState = combat.State };
                    enemy.SetMaxHpInternal(100); enemy.SetCurrentHpInternal(100);
                    YamotoKokiIntentUpdatePatch.Postfix(node, attack, enemy);
                    Require(icon.Material == originalIcon && particles.Material == originalParticles,
                        "Hostile Sawatari/enemy intent retained the friendly material.");
                }
            }
            finally { node.Free(); }
        }
        GD.Print("PASS friendly yellow native attack materials, nonattack reuse and hostile restoration");
    }
}
