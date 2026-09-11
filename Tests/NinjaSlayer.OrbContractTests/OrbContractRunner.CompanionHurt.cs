using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Monsters;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static NCombatRoom? _hurtRoom;

    private async Task VerifySawatariHurt(OrbCombat combat, Node stage)
    {
        var harmony = new Harmony("NinjaSlayer.OrbContracts.CompanionHurt");
        _hurtRoom = new NCombatRoom();
        harmony.Patch(AccessTools.PropertyGetter(typeof(NCombatRoom), nameof(NCombatRoom.Instance)),
            prefix: new HarmonyMethod(typeof(OrbContractRunner), nameof(ResolveHurtRoom)));
        harmony.Patch(AccessTools.Method(typeof(NCombatRoom), nameof(NCombatRoom.GetCreatureNode), [typeof(Creature)]),
            prefix: new HarmonyMethod(typeof(OrbContractRunner), nameof(ResolveHurtActor)));
        const string path = "res://NinjaSlayer/scenes/creature_visuals/sawatari.tscn";
        PreloadManager.Cache.SetAsset(path, GD.Load(path));
        try
        {
            foreach (CombatSide side in new[] { CombatSide.Enemy, CombatSide.Player })
            {
                var creature = new Creature(ModelDb.Monster<SawatariMonster>().ToMutable(), side, null)
                {
                    CombatState = combat.State,
                    PetOwner = side == CombatSide.Player ? combat.Player : null
                };
                creature.SetMaxHpInternal(100);
                creature.SetCurrentHpInternal(100);
                var actor = new AimContractCreature { Position = new(400f, 60f) };
                NCreatureVisuals rig = STS2RitsuLib.Scaffolding.Godot.RitsuGodotNodeFactories
                    .CreateFromScenePath<NCreatureVisuals>(path)!;
                AccessTools.Property(typeof(NCreature), "Entity").SetValue(actor, creature);
                AccessTools.Property(typeof(NCreature), "Visuals").SetValue(actor, rig);
                actor.AddChild(rig);
                AimActors.Add(creature, actor);
                stage.AddChild(actor);
                Node2D anchor = rig.GetNode<Node2D>("AirborneAnchor");
                Transform2D baseline = anchor.Transform;
                Vector2 rootBaseline = actor.Position;
                try
                {
                    Task hit = Task.CompletedTask;
                    Require(!NinjaSlayerAnimationPatch.Prefix(creature, "Hit", 0f, ref hit),
                        "Sawatari Hit was not handled by the mod animation route.");
                    if (side == CombatSide.Enemy)
                    {
                        Require(hit.IsCompletedSuccessfully && StaggerAnimation.IsActive(creature),
                            "Enemy Sawatari must start a nonblocking shared stagger.");
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                        Require(actor.Position.X > rootBaseline.X && anchor.RotationDegrees > 0f,
                            "Enemy Sawatari did not recoil and lean away from the player.");
                        while (StaggerAnimation.IsActive(creature))
                            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                        Require(actor.Position.IsEqualApprox(rootBaseline) && anchor.Transform.IsEqualApprox(baseline),
                            "Enemy Sawatari did not restore after hurt.");
                        for (int hitIndex = 0; hitIndex < 2; hitIndex++)
                        {
                            NinjaSlayerAnimationPatch.Prefix(creature, "Hit", 0f, ref hit);
                            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                        }
                        StaggerAnimation.Reset(creature);
                        Require(actor.Position.IsEqualApprox(rootBaseline) && anchor.Transform.IsEqualApprox(baseline),
                            "Repeated/interrupted hurt left Sawatari displaced.");
                        NinjaSlayerAnimationPatch.Prefix(creature, "BlockedHit", .05f, ref hit);
                        Require(!StaggerAnimation.IsActive(creature) && hit.IsCompletedSuccessfully,
                            "Fully blocked damage should shake, not stagger Sawatari.");
                        await ToSignal(GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout);
                        Require(rig.Position.IsEqualApprox(Vector2.Zero) && actor.Position.IsEqualApprox(rootBaseline),
                            "Blocked hurt did not restore Sawatari.");
                    }
                    else
                    {
                        Require(!StaggerAnimation.IsActive(creature), "Allied Sawatari incorrectly staggered.");
                        await ToSignal(GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout);
                        Require(actor.Position.X < rootBaseline.X && anchor.Transform.IsEqualApprox(baseline),
                            "Allied Sawatari must retain the existing dodge without leaning.");
                        await hit;
                        Require(actor.Position.IsEqualApprox(rootBaseline), "Allied dodge did not return home.");
                    }
                }
                finally
                {
                    StaggerAnimation.Reset(creature);
                    AimActors.Remove(creature);
                    actor.Free();
                }
            }
            GD.Print("PASS Sawatari actual hurt: enemy shared stagger, repeated-hit cleanup, blocked shake and unchanged allied dodge.");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            _hurtRoom.Free();
            _hurtRoom = null;
        }
    }

    private static bool ResolveHurtRoom(ref NCombatRoom? __result)
    {
        __result = _hurtRoom;
        return false;
    }

    private static bool ResolveHurtActor(Creature __0, ref NCreature? __result)
    {
        __result = AimActors.GetValueOrDefault(__0);
        return false;
    }
}
