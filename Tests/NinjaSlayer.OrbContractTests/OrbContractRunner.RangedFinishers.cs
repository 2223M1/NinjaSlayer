using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Orbs;
using NinjaSlayer.Monsters;
using STS2RitsuLib;
using STS2RitsuLib.Patching.Core;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static readonly Type RangedActionType = typeof(ShurikenOrb).Assembly.GetType(
        "NinjaSlayer.Code.ExternalAnimations.FinisherRangedAction", true)!;
    private static Action<Creature?>? _observeRangedDamage;

    private static void ObserveRangedDamage(Creature? dealer) => _observeRangedDamage?.Invoke(dealer);

    private async Task VerifyRangedVisualFreeze(NCreature target)
    {
        var room = new NCombatRoom();
        var container = new Control();
        AddChild(container);
        AccessTools.Property(typeof(NCombatRoom), "CombatVfxContainer").SetValue(room, container);
        Control? oldHitbox = target.Hitbox;
        var hitbox = new Control { Size = new(60, 60) };
        target.AddChild(hitbox);
        AccessTools.Property(typeof(NCreature), "Hitbox").SetValue(target, hitbox);
        var projectile = new Node2D { Position = new(-3000, -3000) };
        var particles = new GpuParticles2D { SpeedScale = .7 };
        var cpu = new CpuParticles2D { SpeedScale = .8f };
        container.AddChild(projectile);
        projectile.AddChild(particles);
        projectile.AddChild(cpu);
        Tween tail = projectile.CreateTween();
        tail.TweenProperty(projectile, "rotation", 1f, 1f);
        Type freeze = typeof(ShurikenOrb).Assembly.GetType("NinjaSlayer.Code.ExternalAnimations.FinisherImpactVfxFreezeLease", true)!;
        try
        {
            using (var lease = (IDisposable)AccessTools.Method(freeze, "Acquire").Invoke(null,
                [room, new[] { target }, new HashSet<ulong> { projectile.GetInstanceId() }, 10f, new Node[] { projectile }])!)
            {
                Require(projectile.ProcessMode == Node.ProcessModeEnum.Disabled
                    && particles.SpeedScale == 0 && cpu.SpeedScale == 0, "Doom did not freeze owned particles outside the hit region.");
                for (int frame = 0; frame < 8; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Require(Mathf.IsZeroApprox(projectile.Rotation), "A bound catch/impact Tween moved during Doom.");
            }
            Require(projectile.ProcessMode == Node.ProcessModeEnum.Inherit
                && Math.Abs(particles.SpeedScale - .7) < .001 && Math.Abs(cpu.SpeedScale - .8f) < .001,
                "Doom cleanup did not restore original process modes and particle speeds.");
            for (int frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(projectile.Rotation > 0, "The projectile's bound tail Tween did not resume.");
        }
        finally
        {
            tail.Kill();
            AccessTools.Property(typeof(NCreature), "Hitbox").SetValue(target, oldHitbox);
            hitbox.QueueFree(); container.QueueFree(); room.Free();
        }
        GD.Print("PASS owned ranged projectile particle freeze, bound catch Tween pause and exact restore.");
    }

    private static async Task VerifyRangedSources()
    {
        var product = typeof(ShurikenOrb).Assembly;
        var patcher = RitsuLibFramework.CreatePatcher("NinjaSlayer.OrbContracts", "ranged_sources");
        foreach (string name in new[] { "FinisherOrbPassivePatch", "FinisherOrbEvokePatch",
            "FinisherShivVisualPatch", "FinisherShivTimingPatch" })
            typeof(ModPatcherExtensions).GetMethod("RegisterPatch")!.MakeGenericMethod(
                product.GetType("NinjaSlayer.Code.Patches." + name, true)!).Invoke(null, [patcher]);
        Require(patcher.PatchAll(), "Ranged native targets failed to install.");
        var observer = new Harmony("NinjaSlayer.OrbContracts.RangedObserver");
        var damage = AccessTools.Method(typeof(CreatureCmd), nameof(CreatureCmd.Damage),
            [typeof(PlayerChoiceContext), typeof(IEnumerable<Creature>), typeof(decimal), typeof(ValueProp),
                typeof(Creature), typeof(CardModel)
#if !NINJASLAYER_CHANNEL_STABLE
                , typeof(MegaCrit.Sts2.Core.Entities.Cards.CardPlay)
#endif
            ]);
        observer.Patch(damage, prefix: new HarmonyMethod(typeof(OrbContractRunner), nameof(ObserveRangedDamage)));
        try
        {
            using (var combat = new OrbCombat(ninjaSlayer: true))
            {
                foreach (var (card, expected) in new (CardModel, bool)[] {
                    (ModelDb.Card<Shiv>(), true), (ModelDb.Card<StrongShurikenTokenRedesignV1>(), true),
                    (ModelDb.Card<SawatariMachete>(), true), (ModelDb.Card<StrikeNinjaSlayerRedesignV1>(), false) })
                    Require((bool)AccessTools.Method(RangedActionType, "IsRangedCard").Invoke(null, [card])! == expected,
                        "Ranged card routing changed: " + card.Id);
                // A scheduled release appends its own arrival after waiting has already begun.
                using var scope = (IDisposable)AccessTools.Method(RangedActionType, "Begin").Invoke(null, [combat.Player.Creature, null])!;
                var release = new TaskCompletionSource();
                var arrival = new TaskCompletionSource();
                AccessTools.Method(RangedActionType, "TrackArrival").Invoke(scope, [release.Task]);
                Task wait = (Task)AccessTools.Method(RangedActionType, "WaitForImpact").Invoke(scope, null)!;
                AccessTools.Method(RangedActionType, "TrackArrival").Invoke(scope, [arrival.Task]);
                release.SetResult();
                Require(!wait.IsCompleted, "Delayed release completed before projectile arrival.");
                arrival.SetResult();
                await wait;
            }
            Require(AccessTools.Property(RangedActionType, "Active").GetValue(null) == null, "Ranged scope leaked.");
            using (var combat = new OrbCombat(ninjaSlayer: true))
            {
                var run = MegaCrit.Sts2.Core.Runs.RunState.CreateForTest([combat.Player], seed: "ranged-monster-contract");
                AccessTools.Field(typeof(CombatState), "<RunState>k__BackingField").SetValue(combat.State, run);
                foreach (var (type, ids) in new (Type, string[])[] {
                    (typeof(CrossbowRubyRaider), ["FIRE_MOVE"]),
                    (typeof(TurretOperator), ["UNLOAD_MOVE", "UNLOAD_MOVE_2"]),
                    (typeof(FakeMerchantMonster), ["SPEW_COINS_MOVE", "THROW_RELIC_MOVE"]),
                    (typeof(Toadpole), ["SPIKE_SPIT_MOVE"]), (typeof(SludgeSpinner), ["OIL_SPRAY_MOVE"]),
                    (typeof(MechaKnight), ["FLAMETHROWER_MOVE"]), (typeof(MagiKnight), ["MAGIC_BOMB"]),
                    (typeof(KinPriest), ["BEAM_MOVE"]), (typeof(Aeonglass), ["EYE_LASERS_MOVE"]),
                    (typeof(TheLost), ["EYE_LASERS"]), (typeof(TorchHeadAmalgam), ["BEAM_MOVE"]),
                    (typeof(DarkNinjaMonster), [DarkNinjaMonster.DeathSlashMoveId]) })
                {
                    var canonical = (MonsterModel)typeof(ModelDb).GetMethod("Monster")!.MakeGenericMethod(type).Invoke(null, null)!;
                    MonsterModel monster = canonical.ToMutable();
                    monster.RunRng = run.Rng;
                    var creature = new Creature(monster, CombatSide.Enemy, null) { CombatState = combat.State };
                    AccessTools.Method(typeof(CombatState), "AttachCreature").Invoke(combat.State, [creature]);
                    combat.State.AddCreature(creature);
                    monster.SetUpForCombat();
                    Require(ids.All(id => monster.MoveStateMachine!.States.ContainsKey(id)), "Native ranged move disappeared: " + type.Name);
                    foreach (MoveState move in monster.MoveStateMachine!.States.Values.OfType<MoveState>())
                    {
                        monster.SetMoveImmediate(move, forceTransition: true);
                        Require((bool)AccessTools.Method(RangedActionType, "IsRangedMove").Invoke(null, [monster])! == ids.Contains(move.Id),
                            "Ranged classification leaked between moves of " + type.Name + ": " + move.Id);
                    }
                }
            }
            foreach (bool ninja in new[] { false, true })
            foreach (string kind in new[] { "lightning", "dark", "glass" })
            {
                using var combat = new OrbCombat(ninjaSlayer: ninja);
                var run = MegaCrit.Sts2.Core.Runs.RunState.CreateForTest([combat.Player], seed: "ranged-orb-contract");
                AccessTools.Field(typeof(CombatState), "<RunState>k__BackingField").SetValue(combat.State, run);
                var second = combat.AddEnemy();
                await (kind switch {
                    "lightning" => OrbCmd.Channel<LightningOrb>(Choice, combat.Player),
                    "dark" => OrbCmd.Channel<DarkOrb>(Choice, combat.Player),
                    _ => OrbCmd.Channel<GlassOrb>(Choice, combat.Player) });
                OrbModel orb = combat.Queue.Orbs.Single();
                int calls = 0;
                _observeRangedDamage = dealer => {
                    calls++;
                    object? scope = AccessTools.Method(RangedActionType, "For").Invoke(null, [dealer]);
                    Require((scope != null) == ninja, "Native orb did not retain only its Ninja Slayer action scope.");
                };
                int Counter() =>
#if NINJASLAYER_CHANNEL_STABLE
                    run.Rng.CombatTargets.Counter;
#else
                    run.Rng.CombatTargets.ToSerializable().counter;
#endif
                int rng = Counter();
                decimal before = orb.PassiveVal;
                await orb.Passive(Choice, null);
                Require(calls == (kind == "dark" ? 0 : 1), "Passive damage call count changed.");
                Require(kind != "glass" || orb.PassiveVal == before - 1, "Glass passive no longer decays natively.");
                await orb.Evoke(Choice);
                await orb.Evoke(Choice);
                Require(calls == (kind == "dark" ? 2 : 3), "Repeated evokes were merged or skipped.");
                Require(Counter() - rng == (kind == "lightning" ? 3 : 0),
                    $"Ranged orb targeting RNG differs: {kind}, ninja={ninja}, delta={Counter() - rng}.");
                if (kind == "dark") Require(second.CurrentHp == 1000 && combat.Enemy.CurrentHp == 976,
                    "Dark no longer hits the native lowest-HP target with its accumulated value.");
                Require(AccessTools.Property(RangedActionType, "Active").GetValue(null) == null,
                    "Completed native orb left a ranged context in its caller.");
                _observeRangedDamage = null;
            }
        }
        finally { _observeRangedDamage = null; observer.UnpatchAll(observer.Id); patcher.UnpatchAll(); }
        GD.Print("PASS exact native ranged/melee move routing, action lifetime, delayed release, Shiv waits and Lightning/Dark/Glass passive + repeated evoke scope, damage and RNG.");
    }
}
