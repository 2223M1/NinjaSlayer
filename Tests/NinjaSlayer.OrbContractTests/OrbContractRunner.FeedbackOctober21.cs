using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Powers;
using STS2RitsuLib;
using STS2RitsuLib.Patching.Core;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private async Task VerifyOctober21Feedback(string mode)
    {
        if (mode is "evasion" or "all")
        {
            var patcher = RitsuLibFramework.CreatePatcher("NinjaSlayer.October21.Evasion", "Product");
            patcher.RegisterPatch<EnemyAttackDodgeScopePatch>();
            patcher.RegisterPatch<EnemyAttackDodgeAnimationPatch>();
            patcher.RegisterPatch<AttackIntentDamagePreviewPatch>();
            foreach (string name in new[] { "EvasionMoveScopePatch", "EvasionPowerApplyPatch" })
                typeof(ModPatcherExtensions).GetMethod("RegisterPatch")!.MakeGenericMethod(
                    typeof(EvasionPower).Assembly.GetType("NinjaSlayer.Code.Patches." + name, true)!).Invoke(null, [patcher]);
            Require(patcher.PatchAll(), "Evasion presentation patches failed to install.");
            try
            {
                foreach (var speed in new[] { FastModeType.Normal, FastModeType.Fast, FastModeType.Instant })
                {
                    SaveManager.Instance.PrefsSave.FastMode = speed;
                    await VerifyRelease113Cards();
                    using var combat = new OrbCombat(ninjaSlayer: true);
                    var owner = combat.Player.Creature;
                    await PowerCmd.Apply<EvasionPower>(Choice, owner, 2, owner, null);
                    await CreatureCmd.GainBlock(owner, 4, ValueProp.Unpowered, null);
                    int hp = owner.CurrentHp;
                    await Hook.BeforeSideTurnStart(combat.State, CombatSide.Enemy, [combat.Enemy]);
                    var move = new MoveState("OCT21_MULTI_ATTACK", async targets =>
                    {
                        await DamageCmd.Attack(10).WithHitCount(3).FromMonster(combat.Enemy.Monster!).Execute(Choice);
                        await CreatureCmd.Damage(Choice, targets, 10, ValueProp.Move, combat.Enemy);
                        Require(owner.GetPowerAmount<EvasionPower>() == 2, "An enemy consumed evasion before finishing its move.");
                        await PowerCmd.Apply<WeakPower>(Choice, owner, 1, combat.Enemy, null);
                    });
                    await move.PerformMove([owner]);
                    Require(owner.CurrentHp == hp && owner.Block == 4 && owner.GetPowerAmount<EvasionPower>() == 1,
                        $"Native enemy attack did not evade under {speed}.");
                    Require(!owner.HasPower<WeakPower>(), "An entirely evaded move still applied its hit debuff.");
                    await new MoveState("OCT21_DEFEND", _ => Task.CompletedTask).PerformMove([owner]);
                    Require(owner.GetPowerAmount<EvasionPower>() == 1, "A non-attacking enemy consumed evasion.");
                    var second = combat.AddEnemy();
                    await new MoveState("OCT21_SECOND", async targets =>
                    {
                        for (int i = 0; i < 3; i++) await CreatureCmd.Damage(Choice, targets, 10, ValueProp.Move, second);
                    }).PerformMove([owner]);
                    Require(owner.CurrentHp == hp && owner.Block == 4 && !owner.HasPower<EvasionPower>(),
                        "Two layers must evade two enemies' complete multihit moves.");
                    await new MoveState("OCT21_THIRD", targets => CreatureCmd.Damage(Choice, targets, 10, ValueProp.Move, second)).PerformMove([owner]);
                    Require(owner.CurrentHp == hp - 6 && owner.Block == 0, "Attacks still evaded after the last layer.");
                    await PowerCmd.Apply<EvasionPower>(Choice, owner, 1, owner, null);
                    await Hook.BeforeSideTurnStart(combat.State, CombatSide.Player, [owner]);
                    Require(!owner.HasPower<EvasionPower>(), "Unused evasion did not expire at the owner's next turn.");
                }
                GD.Print("PASS October21 evasion: direct/native attacks, HP/block, layers and native side-turn expiry at all three speeds.");
            }
            finally { patcher.UnpatchAll(); }
        }
        if (mode is "dark" or "all")
        {
            string first = await October21DarkStrikeTranscript(false);
            string mirrored = await October21DarkStrikeTranscript(true);
            Require(first == mirrored, "Dark Strike changes theft/HP/RNG when local player positions are swapped.");
            GD.Print("PASS October21 Dark Strike: opposite local player layouts preserve target order, stolen cards, HP and RNG.");
        }
        if (mode is "blood" or "all") await VerifyOctober21ForeignBloodRig();
        if (mode is "anti-air" or "all")
        {
            foreach (bool upgraded in new[] { false, true })
            foreach (int life in new[] { 0, 1, 20 })
            {
                using var combat = new OrbCombat(ninjaSlayer: true);
                if (life > 0) await PowerCmd.Apply<NarakuLifePower>(Choice, combat.Player.Creature, life, combat.Player.Creature, null);
                var fist = AddCard<AntiAirBangBangFist>(combat, upgraded: upgraded);
                int hits = life > 0 ? 2 : 1;
                Require(((CalculatedVar)fist.DynamicVars["CalculatedHits"]).Calculate(null) == hits,
                    "Anti-Air preview omits Naraku Life or counts its stacks instead of its type.");
                await CardCmd.AutoPlay(Choice, fist, null);
                Require(combat.Enemy.CurrentHp == 1000 - hits * (upgraded ? 11 : 8), "Anti-Air actual hits differ from its Naraku Life preview.");
            }
            GD.Print("PASS October21 Anti-Air: base/upgrade dynamic preview and actual damage count Naraku Life once at 1/20 stacks.");
        }
    }

    private async Task<string> October21DarkStrikeTranscript(bool mirrored)
    {
        using var f = new DarkStrikeFixture(multiplayer: true);
        var harmony = new Harmony("NinjaSlayer.October21.DarkStrike");
        _hurtRoom = new NCombatRoom();
        harmony.Patch(AccessTools.PropertyGetter(typeof(NCombatRoom), "Instance"),
            prefix: new HarmonyMethod(GetType(), nameof(ResolveHurtRoom)));
        harmony.Patch(AccessTools.Method(typeof(NCombatRoom), nameof(NCombatRoom.GetCreatureNode), [typeof(Creature)]),
            prefix: new HarmonyMethod(GetType(), nameof(ResolveHurtActor)));
        var actors = new List<NCreature>();
        try
        {
            foreach (var player in f.Run.Players)
            {
                foreach (CardModel card in new CardModel[] { ModelDb.Card<NinjaCaltrops>(), ModelDb.Card<Composure>(), ModelDb.Card<Macaco>() })
                    Stealable(f, card, owner: player);
                var actor = new AimContractCreature();
                var visuals = new AimContractVisuals();
                var body = new Node2D();
                visuals.AddChild(body);
                AccessTools.Field(typeof(NCreatureVisuals), "_body").SetValue(visuals, body);
                var marker = new Marker2D();
                visuals.AddChild(marker);
                AccessTools.Property(typeof(NCreatureVisuals), "VfxSpawnPosition").SetValue(visuals, marker);
                var bounds = new Control { Position = new((player == f.Combat.Player ^ mirrored) ? 100 : 500, 0), Size = new(100, 100) };
                visuals.AddChild(bounds);
                AccessTools.Property(typeof(NCreatureVisuals), "Bounds").SetValue(visuals, bounds);
                AccessTools.Property(typeof(NCreature), "Entity").SetValue(actor, player.Creature);
                AccessTools.Property(typeof(NCreature), "Visuals").SetValue(actor, visuals);
                actor.AddChild(visuals);
                AddChild(actor);
                actors.Add(actor);
                AimActors.Add(player.Creature, actor);
            }
            SaveManager.Instance.PrefsSave.FastMode = FastModeType.Instant;
            await DarkStrike(f.Monster, f.Target, f.Other!.Creature);
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                Stolen = f.Monster.Creature.Powers.OfType<SwipePower>().Select(p => new { p.StolenCard!.Owner.NetId, Id = p.StolenCard.Id.ToString() }),
                Hp = f.Run.Players.Select(p => p.Creature.CurrentHp),
                Rng = f.Run.Rng.ToSerializable()
            });
        }
        finally
        {
            foreach (var actor in actors) { AimActors.Remove(actor.Entity); actor.Free(); }
            harmony.UnpatchAll(harmony.Id);
            _hurtRoom.Free();
            _hurtRoom = null;
        }
    }

    private async Task VerifyOctober21ForeignBloodRig()
    {
        using var combat = new OrbCombat(ninjaSlayer: true);
        var harmony = new Harmony("NinjaSlayer.October21.Blood");
        _hurtRoom = new NCombatRoom();
        var effects = new Control();
        AddChild(effects);
        AccessTools.Property(typeof(NCombatRoom), "CombatVfxContainer").SetValue(_hurtRoom, effects);
        harmony.Patch(AccessTools.PropertyGetter(typeof(NCombatRoom), "Instance"),
            prefix: new HarmonyMethod(GetType(), nameof(ResolveHurtRoom)));
        harmony.Patch(AccessTools.Method(typeof(Creature), nameof(Creature.GetCreatureNode)),
            prefix: new HarmonyMethod(GetType(), nameof(ResolveAimActor)));
        var actor = new AimContractCreature();
        var visuals = new AimContractVisuals();
        actor.AddChild(visuals);
        AccessTools.Property(typeof(NCreature), "Entity").SetValue(actor, combat.Player.Creature);
        AccessTools.Property(typeof(NCreature), "Visuals").SetValue(actor, visuals);
        AddChild(actor);
        AimActors.Add(combat.Player.Creature, actor);
        try
        {
            SaveManager.Instance.PrefsSave.FastMode = FastModeType.Normal;
            var type = typeof(EvasionPower).Assembly.GetType("NinjaSlayer.Code.Nodes.NLowHealthBloodVfx", true)!;
            AccessTools.Method(type, "Emit").Invoke(null, [combat.Player.Creature]);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(effects.GetChildCount() == 0, "A foreign visual rig received a NinjaSlayer-specific mouth effect.");
            var source = new Sprite2D { Name = "Visuals" };
            visuals.AddChild(source);
            source.Owner = visuals;
            source.UniqueNameInOwner = true;
            AccessTools.Method(type, "Emit").Invoke(null, [combat.Player.Creature]);
            Require(effects.GetChildCount() == 1, "The authored sprite no longer receives its mouth effect.");
            var effect = (Node2D)effects.GetChild(0);
            effect._Process(.05);
            source.Free();
            AccessTools.Method(type, "BeforeDraw").Invoke(effect, null);
            Require(effect.IsQueuedForDeletion(), "Removing the source sprite leaves a live mouth effect.");
            GD.Print("PASS October21 low-health blood: foreign/copied visuals cannot create an invalid mouth effect or interrupt damage.");
        }
        finally
        {
            AimActors.Remove(combat.Player.Creature);
            actor.Free(); effects.Free();
            harmony.UnpatchAll(harmony.Id);
            _hurtRoom.Free(); _hurtRoom = null;
        }
    }
}
