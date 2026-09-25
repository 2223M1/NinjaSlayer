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
        VerifyFinisherImpactGeometry();
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

    private void VerifyFinisherImpactGeometry()
    {
        Type session = typeof(ShurikenOrb).Assembly.GetType("NinjaSlayer.Code.ExternalAnimations.FinisherSession", true)!;
        Type snapshot = session.GetNestedType("ImpactVisualSnapshot", System.Reflection.BindingFlags.NonPublic)!;
        var parent = new Node2D { Rotation = 0.21f };
        var body = new Node2D { Transform = new Transform2D(0.17f, new(-1.2f, 0.8f), 0.13f, new(20, 30)) };
        var bounds = new Control { Position = new(-50, -100), Size = new(100, 100) };
        AddChild(parent); parent.AddChild(body); parent.AddChild(bounds);
        Transform2D original = body.Transform;
        try
        {
            foreach (float angle in new[] { 0f, Mathf.Pi / 4f, Mathf.Pi / 2f, Mathf.Pi, -Mathf.Pi / 3f })
            {
                body.Transform = original;
                Vector2 axis = Vector2.FromAngle(angle);
                Vector2 normal = axis.Orthogonal();
                Transform2D before = body.GlobalTransform;
                object sample = Activator.CreateInstance(snapshot,
                    [body, body.Position, body.Scale, body.Rotation, Colors.White, bounds, 1f, axis, new Vector2(.55f, 1.2f), null])!;
                object state = AccessTools.Method(session, "CaptureDeathSquashState").Invoke(null,
                    [sample, new Vector2(.55f, 1.2f)])!;
                AccessTools.Method(session, "ApplyDeathSquashTransform").Invoke(null,
                    [state, new Vector2(.55f, 1.2f), body.Rotation]);
                Transform2D change = body.GlobalTransform * before.AffineInverse();
                Require(change.BasisXform(axis).DistanceTo(axis * .55f) < .001f,
                    "Squash axis was affected by a mirrored or rotated parent.");
                Require(change.BasisXform(normal).DistanceTo(normal * 1.2f) < .001f,
                    "Squash lost its perpendicular expansion.");
            }
            Type timeline = typeof(ShurikenOrb).Assembly.GetType("NinjaSlayer.Code.ExternalAnimations.FinisherTimeline", true)!;
            float previous = 1f;
            for (int i = 0; i <= 8; i++)
            {
                float zoom = (float)AccessTools.Method(timeline, "ImpactZoom").Invoke(null, [1f, 2.12f, i / 8f])!;
                Require(zoom >= previous && zoom <= 2.1201f, "Impact camera curve overshot or reversed.");
                previous = zoom;
            }
            Require(Math.Abs(previous - 2.12f) < .0001f, "Impact camera failed to reach its authored endpoint.");
            Type profiles = timeline.Assembly.GetType("NinjaSlayer.Code.ExternalAnimations.FinisherPreviewProfile", true)!;
            Require(AccessTools.Property(timeline, "PreviewProfile").GetValue(null)!.ToString() == "B",
                "The normal game must default to the B impact camera timeline.");
            float lastBoundary = 0f;
            foreach (var (name, referenceFrame) in new[] {
                ("ImpactZoomSeconds", 4), ("ImpactReturnStart", 26),
                ("ImpactReleaseSeconds", 27), ("ImpactEndSeconds", 31) })
            {
                var method = AccessTools.Method(timeline, name);
                float full = (float)method.Invoke(null, [Enum.Parse(profiles, "C")])!;
                float half = (float)method.Invoke(null, [Enum.Parse(profiles, "B")])!;
                Require(Math.Abs(full - referenceFrame * 1001f / 30000f) < .00001f
                    && Math.Abs(half * 2f - full) < .00001f,
                    "B/C phases no longer share the measured reference clock: " + name);
                Require(full > lastBoundary, "Camera return must start before release and finish afterwards.");
                lastBoundary = full;
            }
        }
        finally { parent.QueueFree(); }
        GD.Print("PASS directional affine squash with mirrored/skewed parents and monotonic measured impact zoom.");
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
