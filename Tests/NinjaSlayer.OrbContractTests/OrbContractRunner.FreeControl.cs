using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Multiplayer;
using NinjaSlayer.Content;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Orbs;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static readonly List<GameAction> FreeImpacts = [];
    private static bool CaptureFreeImpact(GameAction action) { FreeImpacts.Add(action); return false; }
    private static bool _freeSetting;
    private static bool FreeSetting(ref bool __result) { __result = _freeSetting; return false; }
    private async Task VerifyFreeControl()
    {
        using var combat = new OrbCombat(ninjaSlayer: true);
        RunState run = RunState.CreateForTest([combat.Player]);
        var harmony = new Harmony("NinjaSlayer.OrbContracts.FreeControl");
        harmony.Patch(AccessTools.Method(typeof(Creature), nameof(Creature.GetCreatureNode)),
            prefix: new HarmonyMethod(typeof(OrbContractRunner), nameof(ResolveAimActor)));
        harmony.Patch(AccessTools.Method(typeof(ActionQueueSynchronizer), nameof(ActionQueueSynchronizer.RequestEnqueue)),
            prefix: new HarmonyMethod(typeof(OrbContractRunner), nameof(CaptureFreeImpact)));
        object oldNet = RunManager.Instance.NetService;
        object oldQueue = RunManager.Instance.ActionQueueSynchronizer;
        AccessTools.Property(typeof(RunManager), "NetService").SetValue(RunManager.Instance, new NetSingleplayerGameService());
        AccessTools.Property(typeof(RunManager), "ActionQueueSynchronizer").SetValue(RunManager.Instance,
            System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ActionQueueSynchronizer)));
        combat.Player.PlayerCombatState!.Phase = PlayerTurnPhase.Play;
        STS2RitsuLib.RitsuLibFramework.EnsureGodotScriptsRegistered(typeof(ShurikenOrb).Assembly,
            STS2RitsuLib.RitsuLibFramework.CreateLogger("Free control contracts"));
        const string path = "res://NinjaSlayer/scenes/creature_visuals/ninja_slayer.tscn";
        PreloadManager.Cache.SetAsset(path, GD.Load(path));
        var viewport = new SubViewport { Size = new(1400, 900), RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled };
        AddChild(viewport);
        var stage = new Node2D(); viewport.AddChild(stage);
        var actor = new AimContractCreature { Position = new(500f, 700f) };
        NCreatureVisuals rig = STS2RitsuLib.Scaffolding.Godot.RitsuGodotNodeFactories.CreateFromScenePath<NCreatureVisuals>(path)!;
        AccessTools.Property(typeof(NCreature), "Entity").SetValue(actor, combat.Player.Creature);
        AccessTools.Property(typeof(NCreature), "Visuals").SetValue(actor, rig);
        actor.AddChild(rig); AimActors.Add(combat.Player.Creature, actor); stage.AddChild(actor);
        Node2D pose = rig.GetNode<Node2D>("%AimPose");
        Node control = pose.GetNode("FreeControl");
        Type type = control.GetType();
        object? Invoke(string name, params object?[] args) => AccessTools.Method(type, name).Invoke(control, args);
        void Set(string name, object value) => AccessTools.Field(type, name).SetValue(control, value);
        T Get<T>(string name) => (T)AccessTools.Field(type, name).GetValue(control)!;
        void Sync() => AccessTools.Method(pose.GetType(), "SyncNow").Invoke(pose, null);
        async Task Frames(int count)
        {
            for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        Vector2 original = actor.Position;
        try
        {
            control.SetProcess(false); // Drive eligibility separately; native physics remains live.
            Invoke("Start");
            Require((bool)AccessTools.Method(type, "WasUsed").Invoke(null, [run])!,
                "Free control must exclude this run from balance uploads.");
            CharacterBody2D walker = Get<CharacterBody2D>("_walker");
            RigidBody2D body = Get<RigidBody2D>("_ragdoll");
            await Frames(8);
            Require(walker.IsOnFloor(), $"Free walker never reached the actual authored ground: {walker.Position}.");
            Vector2 start = walker.Position;
            Set("_right", true); await Frames(12); Set("_right", false);
            Require(walker.Position.X > start.X + 45f, "Free movement did not move the body across the arena.");
            Require(actor.Position.IsEqualApprox(original), "Free movement contaminated the card animation's authored root.");
            Set("_jumpPressed", true); Set("_jumpHeld", true); await Frames(8);
            Require(walker.Position.Y < start.Y - 50f, "Jump did not lift the physical body.");
            Set("_jumpPressed", true); await Frames(1);
            Require(walker.Velocity.Y < -750f, "Air jump did not launch again.");
            Set("_jumpHeld", false); await Frames(80);
            Require(walker.IsOnFloor() && Math.Abs(walker.Position.Y - start.Y) < 1f,
                "Walking and double jump did not return to the original floor.");

            // Lift by an off-center real body point, then keep the mouse completely still.
            Vector2 grab = new(-40f, -70f);
            Set("_grabLocal", grab);
            Invoke("Grab");
            var joint = Get<PinJoint2D>("_joint");
            var anchor = Get<StaticBody2D>("_mouseBody");
            Vector2 fixedPoint = anchor.Position - new Vector2(0f, 280f);
            control.SetPhysicsProcess(false); // Hold the cursor anchor fixed, do not inject a mouse swing.
            anchor.Position = fixedPoint;
            body.Position -= new Vector2(0f, 280f);
            await Frames(80);
            Require(joint.MotorEnabled == false && joint.AngularLimitEnabled == false,
                "Grip must remain a passive unconstrained joint.");
            Require((body.Transform * grab).DistanceTo(fixedPoint) < 3f,
                $"The physical grip slipped away from the clicked point: {(body.Transform * grab).DistanceTo(fixedPoint)}px.");
            Require(Math.Abs(body.Rotation) > .1f, "Gravity did not rotate the off-center body around the grip.");
            Require(body.Position.Y > fixedPoint.Y, "Suspended body did not hang below the grip.");
            body.AngularVelocity = 12f;
            Invoke("ReleaseGrip");
            await Frames(2);
            Require(Math.Abs(body.AngularVelocity) > 5f, "Releasing the mouse erased physical angular momentum.");
            Set("_time", .1f); Sync();
            Node2D exposure = Get<Node2D>("_blur");
            Require(exposure.Visible && exposure.GetChildCount() == 1,
                "Actual thrown angular velocity did not produce the planar exposure renderer.");
            using ((IDisposable)Invoke("SuspendForCinematic", original)!)
            {
                Require(!exposure.Visible, "A frozen free-rotation afterimage remained visible during a cinematic.");
                Vector2 claimed = actor.Position;
                Require(Invoke("SuspendForCinematic", original) == null && actor.Position == claimed,
                    "Nested cinematics applied the free offset twice.");
            }
            control.SetPhysicsProcess(true);
            Set("_right", true); await Frames(20); Set("_right", false);
            Require(!Get<bool>("_ragging") && Math.Abs(walker.Rotation) < .001f,
                "Keyboard movement did not resume smoothly after throwing.");
            Sync();
            Vector2 freeCore = rig.VfxSpawnPosition.GetGlobalTransformWithCanvas().Origin;
            var lease = (IDisposable)Invoke("SuspendForCinematic", original)!;
            Require(actor.Position != original, "Exclusive animation did not inherit the free position.");
            lease.Dispose(); Sync();
            Require(actor.Position.IsEqualApprox(original)
                && rig.VfxSpawnPosition.GetGlobalTransformWithCanvas().Origin.DistanceTo(freeCore) < .5f,
                "Exclusive animation did not restore its free-control position.");

            control.SetPhysicsProcess(false);
            var target = new AimContractCreature();
            var hitbox = new Control { Position = new(-30f, -30f), Size = new(60f, 60f) };
            var targetRig = new AimContractVisuals();
            var targetCore = new Marker2D(); targetRig.AddChild(targetCore);
            AccessTools.Property(typeof(NCreatureVisuals), "VfxSpawnPosition").SetValue(targetRig, targetCore);
            AccessTools.Property(typeof(NCreature), "Entity").SetValue(target, combat.Enemy);
            AccessTools.Property(typeof(NCreature), "Visuals").SetValue(target, targetRig);
            AccessTools.Property(typeof(NCreature), "Hitbox").SetValue(target, hitbox);
            target.AddChild(targetRig); target.AddChild(hitbox); stage.AddChild(target);
            AimActors.Add(combat.Enemy, target);
            target.Position = freeCore + new Vector2(700f, 0f);
            Invoke("BeginAttack", 0); Invoke("AdvanceAttacks", .15f); Sync(); Invoke("ReadHull"); Invoke("DetectHits", 1f / 60f);
            Require(FreeImpacts.Count == 0, "Free light attack damaged an enemy outside its physical reach.");
            target.Position = rig.VfxSpawnPosition.GetGlobalTransformWithCanvas().Origin;
            Get<System.Collections.IDictionary>("_enemyPositions").Clear(); // Test relocation is not enemy motion.
            walker.Velocity = Vector2.Zero;
            Invoke("DetectHits", 1f / 60f);
            Require(FreeImpacts.Count == 1, "Contact did not enqueue exactly one native gameplay action.");
            var impacts = FreeImpacts.ToArray(); FreeImpacts.Clear();
            decimal hp = combat.Enemy.CurrentHp;
            foreach (GameAction impact in impacts) await (Task)AccessTools.Method(impact.GetType(), "ExecuteAction").Invoke(impact, null)!;
            Require(combat.Enemy.CurrentHp == hp - 6m, $"Light contact expected 6 damage, got {hp - combat.Enemy.CurrentHp}.");
            Invoke("DetectHits", 1f / 60f);
            Require(FreeImpacts.Count == 0, "Same contact was counted twice inside the cooldown.");
            Invoke("ClearAttacks");
            walker.Position -= new Vector2(0f, 200f); Sync();
            AccessTools.Method(pose.GetType(), "BeginAction").Invoke(pose, [combat.Enemy, false, false]);
            AccessTools.Method(pose.GetType(), "SetTravel").Invoke(pose, [new Vector2(0f, 80f), 1f]);
            Require((float)AccessTools.Property(pose.GetType(), "VerticalTravel").GetValue(pose)! > 79f,
                "A card's downward lunge was incorrectly clamped as grounded during a physical jump.");
            AccessTools.Method(pose.GetType(), "Reset").Invoke(pose, null);
            walker.Position += new Vector2(0f, 200f); Sync();
            target.Position = new(1300f, 100f);
            Invoke("BeginThrow");
            Require(Get<System.Collections.ICollection>("_projectiles").Count == 1, "Free throw failed without stock.");
            Invoke("BeginThrow");
            Require(Get<System.Collections.ICollection>("_projectiles").Count == 1, "Free throw ignored its press cooldown.");
            object shot = Get<System.Collections.IList>("_projectiles")[0]!;
            AccessTools.Field(shot.GetType(), "Released").SetValue(shot, true);
            AccessTools.Field(shot.GetType(), "Origin").SetValue(shot, target.Position - new Vector2(100f, 0f));
            AccessTools.Field(shot.GetType(), "Previous").SetValue(shot, target.Position - new Vector2(100f, 0f));
            AccessTools.Field(shot.GetType(), "End").SetValue(shot, target.Position + new Vector2(100f, 0f));
            AccessTools.Field(shot.GetType(), "Elapsed").SetValue(shot, .233f);
            Invoke("DetectHits", 1f / 60f);
            Require(FreeImpacts.Count == 1, "Free shuriken did not contact the enemy on its actual segment.");
            hp = combat.Enemy.CurrentHp;
            await (Task)AccessTools.Method(FreeImpacts[0].GetType(), "ExecuteAction").Invoke(FreeImpacts[0], null)!;
            Require(combat.Enemy.CurrentHp == hp - 6m, "Free shuriken must deal 6 damage without inventory.");
            FreeImpacts.Clear();
            Invoke("EndTurn"); Sync();
            Require(!(bool)AccessTools.Property(type, "Active").GetValue(control)!
                && actor.Position.IsEqualApprox(original), "Turn end left free control or root movement active.");
            Require(rig.VfxSpawnPosition.GetGlobalTransformWithCanvas().Origin.DistanceTo(freeCore) > 1f,
                "Turn end did not remove the physical offset.");
            Require(Get<System.Collections.ICollection>("_projectiles").Count == 0
                && Get<System.Collections.ICollection>("_throwMotions").Count == 0, "Turn end retained a free projectile or throw motion.");
            AimActors.Remove(combat.Enemy);
            GD.Print("PASS free control: actual Godot floor, locomotion, double jump, passive gravity grip, release momentum, cinematic lease and turn reset.");
            await VerifyFreeShivParticles(stage);
            harmony.Patch(AccessTools.PropertyGetter(typeof(NinjaSlayerSettings), "FreeControlEnabled"),
                prefix: new HarmonyMethod(typeof(OrbContractRunner), nameof(FreeSetting)));
            _hurtRoom = new NCombatRoom();
            harmony.Patch(AccessTools.PropertyGetter(typeof(NCombatRoom), nameof(NCombatRoom.Instance)),
                prefix: new HarmonyMethod(typeof(OrbContractRunner), nameof(ResolveHurtRoom)));
            _freeSetting = true;
            control._Process(.016);
            Require(!(bool)AccessTools.Property(type, "Active").GetValue(control)!, "Mode restarted after end-turn in the same turn.");
            AccessTools.Property(combat.Player.PlayerCombatState!.GetType(), "TurnNumber").SetValue(combat.Player.PlayerCombatState, 2);
            control._Process(.016);
            Require((bool)AccessTools.Property(type, "Active").GetValue(control)!, "Mode did not enable in the next single-player Play phase.");
            _freeSetting = false; control._Process(.016);
            Require(!(bool)AccessTools.Property(type, "Active").GetValue(control)!, "Disabling the setting retained the body.");
            _freeSetting = true;
            combat.Player.PlayerCombatState.Phase = PlayerTurnPhase.None; control._Process(.016);
            Require(!(bool)AccessTools.Property(type, "Active").GetValue(control)!, "Mode activated during enemy phase.");
            combat.Player.PlayerCombatState.Phase = PlayerTurnPhase.Play;
            AccessTools.Property(typeof(RunManager), "NetService").SetValue(RunManager.Instance,
                System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(NetHostGameService)));
            control._Process(.016);
            Require(!(bool)AccessTools.Property(type, "Active").GetValue(control)!, "Free damage mode activated in multiplayer.");
            GD.Print("PASS free control gates: default/setting, immediate disable, player phase, turn generation and single-player-only.");
            Require((bool)AccessTools.Method(type, "WasUsed").Invoke(null, [run])!,
                "Disabling free control must not restore balance eligibility.");
        }
        finally
        {
            Invoke("Stop");
            AimActors.Remove(combat.Player.Creature);
            AimActors.Remove(combat.Enemy);
            AccessTools.Property(typeof(RunManager), "NetService").SetValue(RunManager.Instance, oldNet);
            AccessTools.Property(typeof(RunManager), "ActionQueueSynchronizer").SetValue(RunManager.Instance, oldQueue);
            FreeImpacts.Clear();
            _hurtRoom?.Free(); _hurtRoom = null;
            _freeSetting = false;
            viewport.Free();
            harmony.UnpatchAll(harmony.Id);
        }
    }

    private async Task VerifyFreeShivParticles(Node stage)
    {
        Type helper = typeof(ShurikenOrb).Assembly.GetType("NinjaSlayer.Cards.ShurikenCombat", true)!;
        Type effectType = typeof(ShurikenOrb).Assembly.GetType("NinjaSlayer.Code.Nodes.FreeShurikenEffect", true)!;
        STS2RitsuLib.RitsuLibFramework.EnsureGodotScriptsRegistered(typeof(MegaCrit.Sts2.Core.Nodes.Vfx.NShivThrowVfx).Assembly,
            STS2RitsuLib.RitsuLibFramework.CreateLogger("Native Shiv contracts"));
        string scene = MegaCrit.Sts2.Core.Nodes.Vfx.NShivThrowVfx.scenePath;
        RegisterMountedUids(scene, []);
        PreloadManager.Cache.SetAsset(scene, GD.Load(scene));
        const string texture = "res://NinjaSlayer/images/projectiles/ninja_slayer_shuriken.png";
        PreloadManager.Cache.SetAsset(texture, GD.Load(texture));
        bool testing = MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn;
        MegaCrit.Sts2.Core.Nodes.Vfx.NShivThrowVfx authored;
        try
        {
            MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn = false;
            authored = (MegaCrit.Sts2.Core.Nodes.Vfx.NShivThrowVfx)AccessTools.Method(helper, "CreateFreeThrowVfx")
                .Invoke(null, [new Vector2(200f, 300f), new Vector2(900f, 300f)])!;
        }
        finally { MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn = testing; }
        var effect = (Node2D)AccessTools.Method(effectType, "TakeParticles").Invoke(null, [authored, 700f])!;
        stage.AddChild(effect);
        try
        {
            var head = effect.GetNode<GpuParticles2D>("throw_container/ShurikenHead");
            var material = (ParticleProcessMaterial)head.ProcessMaterial;
            Require(Math.Abs(material.InitialVelocityMin * .15f - 700f) < .5f,
                "Free projectile velocity does not reach its physical endpoint in .15s.");
            Require(material.Color == Colors.White && Math.Abs(material.AngularVelocityMin - 2400f) < .1f,
                "Free projectile changed the gray shuriken tint or full-turn rotation.");
            Require(head.GlobalPosition.DistanceTo(new Vector2(200f, 300f)) < .5f,
                "Free projectile particles do not start at the hand origin.");
            AccessTools.Method(effectType, "Hit").Invoke(effect, [new Vector2(800f, 300f)]);
            Require(!head.Emitting, "First contact did not stop the projectile emitter.");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print("PASS free shuriken: 6 native damage, original Shiv emitters, hand origin, real-time flight speed, gray rotating head and contact-driven impact.");
        }
        finally { effect.Free(); }
    }

    // Mounted host packs don't import their editor UID cache into this isolated project.
    private static void RegisterMountedUids(string path, HashSet<string> visited)
    {
        if (!visited.Add(path) || !(path.EndsWith(".tscn") || path.EndsWith(".tres"))) return;
        string text = Godot.FileAccess.GetFileAsString(path);
        foreach (System.Text.RegularExpressions.Match resource in System.Text.RegularExpressions.Regex.Matches(text, @"\[ext_resource ([^\]]+)\]"))
        {
            var attributes = System.Text.RegularExpressions.Regex.Matches(resource.Groups[1].Value, "(\\w+)=\"([^\"]*)\"")
                .Cast<System.Text.RegularExpressions.Match>().ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
            if (!attributes.TryGetValue("path", out string? dependency)) continue;
            if (attributes.TryGetValue("uid", out string? uid))
            {
                long id = ResourceUid.TextToId(uid);
                if (!ResourceUid.HasId(id)) ResourceUid.AddId(id, dependency);
            }
            RegisterMountedUids(dependency, visited);
        }
    }
}
