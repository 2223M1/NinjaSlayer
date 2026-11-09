using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Orbs;
using NinjaSlayer.Powers;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static async Task VerifyTemporaryHpSemantics()
    {
        foreach (bool overflow in new[] { false, true })
        foreach (bool buffer in new[] { false, true })
        {
            using var combat = new OrbCombat();
            Creature owner = combat.Player.Creature;
            owner.SetCurrentHpInternal(40);
            await PowerCmd.Apply<NarakuLifePower>(Choice, owner, 5, owner, null);
            if (buffer) await PowerCmd.Apply<BufferPower>(Choice, owner, 1, owner, null);
            else combat.Player.AddRelicInternal(ModelDb.Relic<TungstenRod>().ToMutable());
            var result = (await CreatureCmd.Damage(Choice, owner, overflow ? 9 : 4,
                ValueProp.Unblockable | ValueProp.Unpowered, owner)).Single();
            int loss = overflow && !buffer ? 3 : 0;
            Require(owner.CurrentHp == 40 - loss && result.UnblockedDamage == loss
                && owner.GetPowerAmount<NarakuLifePower>() == (overflow ? 0 : 1)
                && owner.HasPower<BufferPower>() == (buffer && !overflow),
                "Temporary HP must absorb self-damage before Tungsten Rod/Buffer, protecting Buffer until overflow.");
        }
        using (var combat = new OrbCombat())
        {
            Creature owner = combat.Player.Creature;
            owner.SetCurrentHpInternal(40);
            await PowerCmd.Apply<NarakuLifePower>(Choice, owner, 120, owner, null);
            await PowerCmd.Apply<NarakuLifePower>(Choice, owner, 20, owner, null);
            int maxHp = owner.MaxHp;
            await CreatureCmd.Heal(owner, 3);
            await CreatureCmd.Damage(Choice, owner, 0, ValueProp.Unpowered, combat.Enemy);
#if NINJASLAYER_CHANNEL_STABLE
            await Hook.AfterTurnEnd(combat.State, CombatSide.Player, [owner]);
#else
            await Hook.AfterSideTurnEnd(combat.State, CombatSide.Player, [owner]);
#endif
            Require(owner.CurrentHp == 43 && owner.MaxHp == maxHp && owner.GetPowerAmount<NarakuLifePower>() == 140,
                "Temporary HP must stack above max HP, survive turn end, and remain separate from healing and real HP.");
            var direct = owner.LoseHpInternal(1, ValueProp.Unblockable | ValueProp.Unpowered);
            Require(direct.UnblockedDamage == 1 && owner.GetPowerAmount<NarakuLifePower>() == 140,
                "Direct HP assignment used by forced death must bypass temporary HP.");
            combat.Player.AfterCombatEnd();
            Require(!owner.HasPower<NarakuLifePower>(), "Native combat cleanup must clear temporary HP.");
        }
        GD.Print("PASS StSLib temporary HP: Block/Intangible before absorption, Buffer/Tungsten after, self-damage, overflow, no false HP-loss hooks, turn retention and combat cleanup.");
    }

    private async Task VerifyArchitectExitBounds()
    {
        using var combat = new OrbCombat(ninjaSlayer: true);
        var harmony = new Harmony("NinjaSlayer.ArchitectExitContract");
        harmony.Patch(AccessTools.PropertyGetter(typeof(NCombatRoom), nameof(NCombatRoom.Instance)),
            prefix: new HarmonyMethod(GetType(), nameof(ResolveHurtRoom)));
        harmony.Patch(AccessTools.Method(typeof(Creature), nameof(Creature.GetCreatureNode)),
            prefix: new HarmonyMethod(GetType(), nameof(ResolveAimActor)));
        STS2RitsuLib.RitsuLibFramework.EnsureGodotScriptsRegistered(typeof(ShurikenOrb).Assembly,
            STS2RitsuLib.RitsuLibFramework.CreateLogger("Architect exit contract"));
        try
        {
            foreach (var (size, zoom) in new[] { (new Vector2I(1280, 720), 1f), (new Vector2I(1920, 1080), .8f), (new Vector2I(2560, 1080), 1.25f) })
            {
                var viewport = new SubViewport { Size = size, TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
                AddChild(viewport);
                _hurtRoom = new FreeInputContractRoom { Size = size };
                var scene = new Control { Size = size, Scale = new Vector2(zoom, zoom), Position = new Vector2(75, 0) };
                _hurtRoom.AddChild(scene);
                AccessTools.Property(typeof(NCombatRoom), nameof(NCombatRoom.SceneContainer)).SetValue(_hurtRoom, scene);
                var actor = new AimContractCreature { Position = new Vector2(850, 600) };
                var visuals = STS2RitsuLib.Scaffolding.Godot.RitsuGodotNodeFactories.CreateFromScenePath<NCreatureVisuals>(
                    "res://NinjaSlayer/scenes/creature_visuals/ninja_slayer.tscn")!;
                AccessTools.Property(typeof(NCreature), nameof(NCreature.Entity)).SetValue(actor, combat.Player.Creature);
                AccessTools.Property(typeof(NCreature), nameof(NCreature.Visuals)).SetValue(actor, visuals);
                actor.AddChild(visuals);
                scene.AddChild(actor);
                AimActors.Add(combat.Player.Creature, actor);
                var cinematic = new ArchitectExecutionCinematic();
                _hurtRoom.AddChild(cinematic);
                foreach (var (field, value) in new (string, object)[]
                {
                    ("_room", _hurtRoom), ("_owner", combat.Player.Creature), ("_ownerNode", actor), ("_architectDeathCommitted", true)
                }) AccessTools.Field(typeof(ArchitectExecutionCinematic), field).SetValue(cinematic, value);
                viewport.AddChild(_hurtRoom);
                try
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    Vector2 slot = actor.Position;
                    await (Task)AccessTools.Method(typeof(ArchitectExecutionCinematic), "ExitScene").Invoke(cinematic, [CancellationToken.None])!;
                    var body = visuals.GetNode<Sprite2D>("%Visuals");
                    Rect2 bounds = body.GetGlobalTransformWithCanvas() * body.GetRect();
                    Require(bounds.Position.X > size.X && actor.Position == slot,
                        $"Architect exit left trailing scarf/texture inside {size} at zoom {zoom}: left={bounds.Position.X}.");
                    GD.Print($"PASS Architect exit: viewport={size}, zoom={zoom}, full texture left={bounds.Position.X}, UI slot unchanged.");
                }
                finally
                {
                    AimActors.Remove(combat.Player.Creature);
                    viewport.Free();
                    _hurtRoom = null;
                }
            }
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
}
