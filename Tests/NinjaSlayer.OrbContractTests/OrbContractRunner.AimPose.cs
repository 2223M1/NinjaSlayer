using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Powers;
using NinjaSlayer.Relics;
using NinjaSlayer.Orbs;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static readonly Dictionary<Creature, NCreature> AimActors = [];

    private async Task VerifyAimPose()
    {
        if (!_hasPresentationResources) throw new InvalidOperationException("Aim contracts require the product scenes.");
        using var combat = new OrbCombat(ninjaSlayer: true);
        MegaCrit.Sts2.Core.Saves.SaveManager.Instance.InitPrefsDataForTest();
        var harmony = new Harmony("NinjaSlayer.OrbContracts.AimPose");
        harmony.Patch(AccessTools.Method(typeof(Creature), nameof(Creature.GetCreatureNode)),
            prefix: new HarmonyMethod(typeof(OrbContractRunner), nameof(ResolveAimActor)));
        var actor = new AimContractCreature();
        var target = new AimContractCreature { Position = new(700f, 0f) };
        STS2RitsuLib.RitsuLibFramework.EnsureGodotScriptsRegistered(typeof(ShurikenOrb).Assembly,
            STS2RitsuLib.RitsuLibFramework.CreateLogger("Aim contracts"));
        foreach (string path in new[]
        {
            "res://NinjaSlayer/scenes/creature_visuals/ninja_slayer.tscn",
            "res://NinjaSlayer/scenes/creature_visuals/yamoto_koki.tscn",
            "res://NinjaSlayer/images/characters/ninja_slayer/kill_idle/NinjaSlayer_kill_idle_0001.png",
            "res://NinjaSlayer/images/characters/ninja_slayer/naraku_idle/NinjaSlayer_naraku_idle_0001.png",
            "res://NinjaSlayer/images/characters/ninja_slayer/naraku.png"
        }) PreloadManager.Cache.SetAsset(path, GD.Load(path));
        NCreatureVisuals rig = STS2RitsuLib.Scaffolding.Godot.RitsuGodotNodeFactories.CreateFromScenePath<NCreatureVisuals>(
            "res://NinjaSlayer/scenes/creature_visuals/ninja_slayer.tscn")!;
        var targetRig = new AimContractVisuals();
        var targetCenter = new Marker2D { Position = new(0f, -300f) };
        targetRig.AddChild(targetCenter);
        AccessTools.Property(typeof(NCreatureVisuals), "VfxSpawnPosition").SetValue(targetRig, targetCenter);
        AccessTools.Property(typeof(NCreature), "Entity").SetValue(actor, combat.Player.Creature);
        AccessTools.Property(typeof(NCreature), "Visuals").SetValue(actor, rig);
        AccessTools.Property(typeof(NCreature), "Entity").SetValue(target, combat.Enemy);
        AccessTools.Property(typeof(NCreature), "Visuals").SetValue(target, targetRig);
        actor.AddChild(rig);
        target.AddChild(targetRig);
        AimActors.Add(combat.Player.Creature, actor);
        AimActors.Add(combat.Enemy, target);
        var stage = new Node2D();
        SubViewport? spinViewport = null;
        if (System.Environment.GetEnvironmentVariable("NINJASLAYER_SPIN_RENDER_DIR") != null
            || System.Environment.GetEnvironmentVariable("NINJASLAYER_DRAG_RENDER_DIR") != null)
        {
            spinViewport = new SubViewport { Size = new(1400, 900), TransparentBg = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            AddChild(spinViewport);
            spinViewport.AddChild(stage);
        }
        else AddChild(stage);
        stage.AddChild(actor);
        stage.AddChild(target);
        Node2D pose = rig.GetNode<Node2D>("%AimPose");
        Node2D anchor = rig.GetNode<Node2D>("AirborneAnchor");
        Sprite2D sprite = rig.GetNode<Sprite2D>("%Visuals");
        Marker2D center = rig.GetNode<Marker2D>("%CenterPos");
        Type poseType = typeof(ShurikenOrb).Assembly.GetType("NinjaSlayer.Code.Nodes.NinjaSlayerAimPose", true)!;
        void Invoke(string method, params object?[] args) => AccessTools.Method(poseType, method).Invoke(pose, args);
        var contour = (System.Numerics.Vector2[])AccessTools.Field(typeof(ShurikenOrb).Assembly.GetType("NinjaSlayer.Code.Combat.CombatBodyContours"), "NinjaSlayer").GetValue(null)!;
        try
        {
            Require(pose.GetType() == poseType, "The packaged AimPose script failed to bind.");
            var facingDrag = new Node();
            stage.AddChild(facingDrag);
            var aimedCard = combat.State.CreateCard<SatsubatsuRedesignV1>(combat.Player);
            Sprite2D facingOverlay = rig.GetNode<Sprite2D>("AirborneAnchor/AimPose/NarakuVisualOverlay");
            foreach (bool locked in new[] { false, true })
            foreach (float pointerX in new[] { 0f, -15f, 15f, -100f, 100f })
            {
                target.Position = new(pointerX, 0f);
                targetCenter.Position = new(0f, -500f);
                Creature? hovered = locked ? combat.Enemy : null;
                Invoke("Drag", facingDrag, aimedCard, new Vector2(pointerX, -500f), hovered);
                float expectedFacing = pointerX == 0f ? Math.Sign(anchor.Scale.X) : Math.Sign(pointerX);
                int flips = 0;
                float lastFacing = Math.Sign(anchor.Scale.X);
                for (int frame = 0; frame < 30; frame++)
                {
                    Invoke("Drag", facingDrag, aimedCard, new Vector2(pointerX, -500f), hovered);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    Invoke("SyncNow");
                    float facing = Math.Sign(anchor.Scale.X);
                    if (facing != lastFacing) { flips++; lastFacing = facing; }
                    Require(flips <= 1, $"Stationary overhead pointer at X={pointerX} repeatedly flipped facing.");
                    Require(sprite.Visible != facingOverlay.Visible,
                        "Overhead aiming must draw exactly one body layer.");
                    Sprite2D visibleBody = facingOverlay.Visible ? facingOverlay : sprite;
                    for (Node? node = visibleBody; node != null; node = node.GetParent())
                        if (node is CanvasItem canvas)
                            Require(Mathf.IsEqualApprox(canvas.Modulate.A, 1f), "Overhead aiming faded the body.");
                    Require(Mathf.IsEqualApprox(visibleBody.SelfModulate.A, 1f), "Overhead aiming faded its body material.");
                }
                if (pointerX != 0f)
                    Require(expectedFacing == Math.Sign(anchor.Scale.X), "Drag turn did not finish facing the pointer side.");
            }
            Invoke("Reset");
            facingDrag.Free();
            GD.Print("PASS overhead aiming: stationary pointer and locked target retain one opaque body and stable facing across scene frames.");
            foreach (float side in new[] { -1f, 1f })
            foreach (float altitude in new[] { 0f, -150f })
            foreach (float height in new[] { -500f, -173f, -40f })
            {
                anchor.Position = new(0f, altitude);
                target.Position = new(side * 700f, 0f);
                targetCenter.Position = new(0f, height);
                Invoke("BeginAction", combat.Enemy, false, false);
                pose._Process(0.05);
                Invoke("SyncNow");
                Vector2 actualCore = sprite.GetGlobalTransformWithCanvas() * new Vector2(580f, 30.30303f);
                Require(actualCore.DistanceTo(center.GetGlobalTransformWithCanvas().Origin) < 0.01f, "Aim core does not follow the body transform.");
                float bottom = contour.Max(p => (sprite.GetGlobalTransformWithCanvas() * new Vector2(p.X, p.Y)).Y);
                Require(Math.Abs(bottom - (altitude - 6.45f)) < 0.5f, $"Body support slipped: {bottom}, altitude {altitude}.");
                Vector2 forward = pose.GetGlobalTransformWithCanvas().BasisXform(Vector2.Right).Normalized();
                Vector2 direction = (targetCenter.GetGlobalTransformWithCanvas().Origin - actualCore).Normalized();
                Require(forward.Dot(direction) > 0.999f, "Ground-corrected aiming does not point at the enemy core.");
            }
            anchor.Position = Vector2.Zero;
            target.Position = new(700f, 0f);
            targetCenter.Position = new(0f, -300f);
            Invoke("BeginTornado", combat.Enemy, false, false);
            Vector2 tornadoRoot = actor.Position;
            float tornadoStartX = center.GlobalPosition.X;
            Invoke("SetTravel", new Vector2(120f, 0f), 1f);
            Invoke("SyncNow");
            Require(actor.Position.IsEqualApprox(tornadoRoot), "Tornado moved the creature root and attached combat UI.");
            Require(Math.Abs(center.GlobalPosition.X - tornadoStartX - 120f) < 0.1f,
                "Tornado did not move its actual hit center with the body.");
            float foot = (sprite.GetGlobalTransformWithCanvas() * new Vector2(295f, 535f)).Y;
            Require(Math.Abs(foot - targetCenter.GlobalPosition.Y) < 0.5f, "Tornado feet did not reach the target core.");
            Vector2 tornadoCore = center.GlobalPosition;
            Invoke("BeginAction", combat.Enemy, false, false);
            Require(!(bool)AccessTools.Property(poseType, "IsTornado").GetValue(pose)!, "The next ordinary attack retained Tornado ownership.");
            Require(center.GlobalPosition.DistanceTo(tornadoCore) < 0.5f, "Tornado handoff dropped the current airborne core.");
            Invoke("BeginReturn");
            Invoke("ApplyReturn", 1f);
            Require(pose.Transform.IsEqualApprox(Transform2D.Identity), "Tornado return left a residual pose.");
            foreach (float targetY in new[] { -400f, -40f })
            {
                actor.Position = Vector2.Zero;
                anchor.Position = new(0f, -150f);
                targetCenter.Position = new(0f, targetY);
                Invoke("BeginAction", combat.Enemy, false, false);
                Invoke("SetTravel", new Vector2(90f, 0f), 1f);
                Invoke("BeginAction", combat.Enemy, true, false);
                Invoke("PlaceAtImpact", combat.Enemy, 600f, true);
                Require(actor.Position.X == 600f, "Finisher no longer places the actor directly at impact.");
                Require(Math.Abs(((Vector2)AccessTools.Property(poseType, "Travel").GetValue(pose)!).X) < 0.01f,
                    "Finisher applied the preceding attack displacement a second time.");
                Vector2 aimedForward = pose.GetGlobalTransformWithCanvas().BasisXform(Vector2.Right).Normalized();
                Vector2 aimedTarget = (targetCenter.GlobalPosition - center.GlobalPosition).Normalized();
                Require(aimedForward.Dot(aimedTarget) > 0.999f, "Finisher impact pose lost its dynamic incoming direction.");
                Vector2 impactCore = center.GlobalPosition;
                int targetHp = combat.Enemy.CurrentHp;
                combat.Enemy.SetCurrentHpInternal(0);
                for (int frame = 0; frame < 30; frame++) Invoke("SyncNow");
                Require(center.GlobalPosition.DistanceTo(impactCore) < 0.5f,
                    "Finisher actor chased its own fallback core after lethal impact.");
                AimActors.Remove(combat.Enemy);
                for (int frame = 0; frame < 30; frame++) Invoke("SyncNow");
                Require(center.GlobalPosition.DistanceTo(impactCore) < 0.5f,
                    "Finisher actor drifted when its victim node was removed.");
                Transform2D stageBefore = stage.Transform;
                Vector2 localImpactCore = stage.ToLocal(impactCore);
                stage.Position += new Vector2(-120f, 35f);
                stage.Scale *= 1.2f;
                for (int frame = 0; frame < 30; frame++) Invoke("SyncNow");
                Require(stage.ToLocal(center.GlobalPosition).DistanceTo(localImpactCore) < 0.5f,
                    "Finisher cached focus did not follow the cinematic camera after victim removal.");
                stage.Transform = stageBefore;
                AimActors.Add(combat.Enemy, target);
                combat.Enemy.SetCurrentHpInternal(targetHp);
                Invoke("BeginReturn");
                Invoke("ApplyReturn", 1f);
            }
            actor.Position = Vector2.Zero;
            anchor.Position = Vector2.Zero;
            Vector2 rigBaseline = rig.Position;
            Vector2 targetBaseline = target.Position;
            var health = new Control { Position = new(10f, 20f) };
            var status = new Control { Position = new(10f, 40f) };
            actor.AddChild(health);
            actor.AddChild(status);
            Vector2 healthBefore = health.GlobalPosition, statusBefore = status.GlobalPosition;
            Invoke("BeginAction", combat.Enemy, true, false);
            Invoke("PlaceAtImpact", combat.Enemy, 600f, false);
            for (int frame = 0; frame < 10; frame++)
            {
                Invoke("SyncNow");
                Require(actor.Position == Vector2.Zero && target.Position == targetBaseline
                    && health.GlobalPosition == healthBefore && status.GlobalPosition == statusBefore,
                    "Alabama visual placement moved a creature root or health/status UI.");
            }
            Require(rig.Position != rigBaseline, "Alabama must still move the body to impact.");
            Invoke("BeginReturn");
            Invoke("ApplyReturn", 1f);
            rig.Position = rigBaseline;
            health.Free(); status.Free();
            await VerifyNativeDrawBatches(combat, pose);
            await VerifyNonblockingThrow(combat, pose);
            await VerifyConcurrentMotion(combat, pose, anchor, center, target);
            var dragOwner = new Node();
            stage.AddChild(dragOwner);
            var tornadoCard = combat.State.CreateCard<TornadoFistRedesignV1>(combat.Player);
            PileType.Hand.GetPile(combat.Player).AddInternal(tornadoCard, -1, silent: true);
            combat.Player.PlayerCombatState!.GainEnergy(4);
            Invoke("Drag", dragOwner, tornadoCard, new Vector2(700f, -300f), combat.Enemy);
            pose._Process(0.3);
            Vector2 chargedTravel = (Vector2)AccessTools.Property(poseType, "Travel").GetValue(pose)!;
            Require(Math.Abs(Math.Abs(chargedTravel.X) - 32f) < 0.01f, "Tornado drag did not reach its authored backstep.");
            Invoke("EndDrag", dragOwner, false);
            await ToSignal(GetTree().CreateTimer(0.25f), SceneTreeTimer.SignalName.Timeout);
            Require(pose.Transform.IsEqualApprox(Transform2D.Identity), "Cancelled drag left its charge pose active.");
            Transform2D unchargedBody = sprite.Transform;
            Invoke("Drag", dragOwner, tornadoCard, new Vector2(700f, -300f), combat.Enemy);
            pose._Process(0.27);
            Invoke("EndDrag", dragOwner, true);
            await NinjaSlayerXAttackSequence.Run(combat.Player.Creature, 0, 0.15f, 0.35f,
                _ => throw new InvalidOperationException("Zero-hit Tornado dealt damage."), heldApproach: true);
            await ToSignal(GetTree().CreateTimer(0.25f), SceneTreeTimer.SignalName.Timeout);
            Require(pose.Transform.IsEqualApprox(Transform2D.Identity), "Zero-hit Tornado retained its charged pose.");
            Require(sprite.Transform.IsEqualApprox(unchargedBody), "Zero-hit Tornado left the body flattened or displaced.");
            Require(!(bool)AccessTools.Property(poseType, "OwnsSpin").GetValue(pose)!, "Zero-hit Tornado retained spin ownership.");
            dragOwner.Free();
            foreach (int formIndex in new[] { 0, 1, 2 })
            {
                if (formIndex == 1)
                    await PowerCmd.Apply<NarakuFormRedesignPower>(Choice, combat.Player.Creature, 1, combat.Player.Creature, null);
                if (formIndex == 2)
                {
                    var relic = ModelDb.Relic<NarakuWithinRelic>().ToMutable();
                    combat.Player.AddRelicInternal(relic);
                }
                foreach (float facing in new[] { -1f, 1f })
                foreach (float stretch in new[] { 0.5f, 1.5f })
                {
                    target.Position = new(700f * facing, 0f);
                    anchor.Scale = new(facing, stretch);
                    anchor.Skew = 0.15f;
                    Invoke("BeginAction", combat.Enemy, false, false);
                    Sprite2D overlay = rig.GetNode<Sprite2D>("AirborneAnchor/AimPose/NarakuVisualOverlay");
                    Sprite2D active = overlay.Visible ? overlay : sprite;
                    Vector2 corePoint = formIndex == 2 ? new(50.8f, 120f) : new(580f, 30.30303f);
                    Require((active.GetGlobalTransformWithCanvas() * corePoint).DistanceTo(center.GetGlobalTransformWithCanvas().Origin) < 0.1f,
                        "Form core lost affine transform tracking.");
                    var kick = combat.State.CreateCard<RoundhouseKickRedesignV1>(combat.Player);
                    var play = new CardPlay { Card = kick,
#if !NINJASLAYER_CHANNEL_STABLE
                        Player = combat.Player,
#endif
                        Target = combat.Enemy, ResultPile = PileType.Discard,
                        Resources = new ResourceInfo { EnergySpent = 0, EnergyValue = 0, StarsSpent = 0, StarValue = 0 },
                        IsAutoPlay = false, PlayIndex = 0, PlayCount = 1 };
                    await (Task)AccessTools.Method(poseType, "PrepareKick").Invoke(pose, [play])!;
                    Vector2 footPoint = formIndex == 2 ? new(422f, 497f) : new(295f, 535f);
                    Vector2 feet = (active.GetGlobalTransformWithCanvas() * footPoint - center.GetGlobalTransformWithCanvas().Origin).Normalized();
                    Vector2 aim = (targetCenter.GetGlobalTransformWithCanvas().Origin - center.GetGlobalTransformWithCanvas().Origin).Normalized();
                    Require(feet.Dot(aim) > 0.999f, "Kick feet do not face the target core.");
                    VerifyAimedGroundShadow(rig, formIndex);
                    Invoke("BeginReturn");
                    Invoke("ApplyReturn", 1f);
                }
                Sprite2D formOverlay = rig.GetNode<Sprite2D>("AirborneAnchor/AimPose/NarakuVisualOverlay");
                VerifyDrawAndThrowPose(combat, pose, anchor, formOverlay.Visible ? formOverlay : sprite, center, target, formIndex);
                await VerifyDragControls(combat, actor, target, targetCenter, rig, formIndex);
                await VerifySpinExposure(rig, formIndex);
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Invoke("Reset");
            actor.Position = Vector2.Zero;
            Node shadowController = rig.GetNode("ShadowController");
            AccessTools.Method(shadowController.GetType(), "SyncNow").Invoke(shadowController, null);
            float groundedShadowY = rig.GetNode<Sprite2D>("Shadow").GlobalPosition.Y;
            AccessTools.Method(shadowController.GetType(), "TrackRootHop").Invoke(shadowController, [rig, 0f]);
            rig.Position = new(0f, -70f);
            AccessTools.Method(shadowController.GetType(), "SyncNow").Invoke(shadowController, null);
            Require(Math.Abs(rig.GetNode<Sprite2D>("Shadow").GlobalPosition.Y - groundedShadowY) < .1f,
                "Visual return hop lifted the shadow off the ground.");
            Require(actor.Position == Vector2.Zero, "Visual return hop moved the health/status root.");
            rig.Position = Vector2.Zero;
            AccessTools.Method(shadowController.GetType(), "SyncNow").Invoke(shadowController, null);
            GD.Print("PASS actual packaged AimPose: high/low targets, both facings, airborne, normal/half/full forms, affine core tracking, kicks, Tornado foot height and return.");
            NCreatureVisuals koki = STS2RitsuLib.Scaffolding.Godot.RitsuGodotNodeFactories.CreateFromScenePath<NCreatureVisuals>(
                "res://NinjaSlayer/scenes/creature_visuals/yamoto_koki.tscn")!;
            stage.AddChild(koki);
            Node2D kokiBody = koki.GetNode<Node2D>("%Visuals");
            Sprite2D kokiSprite = kokiBody.GetNode<Sprite2D>("Sprite");
            Marker2D kokiCenter = koki.VfxSpawnPosition;
            Vector2 centerBaseline = kokiCenter.Position;
            Vector2 bodyBaseline = kokiBody.Position;
            var kokiContour = (System.Numerics.Vector2[])AccessTools.Field(typeof(ShurikenOrb).Assembly.GetType("NinjaSlayer.Code.Combat.CombatBodyContours"), "YamotoKoki").GetValue(null)!;
            MethodInfo tilt = AccessTools.Method(typeof(ShurikenOrb).Assembly.GetType("NinjaSlayer.Code.ExternalAnimations.YamotoKokiCombatAnimations"), "TweenTilt");
            MethodInfo withFacing = AccessTools.Method(typeof(ShurikenOrb).Assembly.GetType("NinjaSlayer.Code.Nodes.YamotoKokiAllyFacingController"), "WithFacing");
            foreach (float facing in new[] { -1f, 1f })
            {
                kokiBody.Transform = Transform2D.Identity;
                kokiBody.Position = bodyBaseline;
                kokiBody.Scale = new(facing, 1f);
                Transform2D authoredBody = kokiBody.Transform;
                async Task TiltWithFacing(float from, float to, float seconds)
                {
                    Task animation = (Task)tilt.Invoke(null, [kokiBody, kokiCenter, centerBaseline, authoredBody, from, to, seconds])!;
                    while (!animation.IsCompleted)
                    {
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                        kokiBody.Transform = (Transform2D)withFacing.Invoke(null, [kokiBody.Transform, facing < 0f])!;
                        Require(kokiBody.Transform.Y.Y > 0f, "Facing updates inverted Koki during her summon tilt.");
                    }
                    await animation;
                }
                Vector2 coreLocal = kokiBody.ToLocal(kokiCenter.GlobalPosition);
                float ground = kokiContour.Max(p => (kokiSprite.GetGlobalTransformWithCanvas() * new Vector2(p.X, p.Y)).Y);
                await TiltWithFacing(0f, 15f, 0.1f);
                float bottom = kokiContour.Max(p => (kokiSprite.GetGlobalTransformWithCanvas() * new Vector2(p.X, p.Y)).Y);
                Require(Math.Abs(bottom - ground) < 0.1f, "Koki contour left its ground line.");
                Require(kokiBody.ToGlobal(coreLocal).DistanceTo(kokiCenter.GlobalPosition) < 0.1f, "Koki summon origin did not follow its pivot.");
                await TiltWithFacing(15f, 0f, 0.2f);
                Require(kokiBody.Position.DistanceTo(bodyBaseline) < 0.01f && kokiCenter.Position.DistanceTo(centerBaseline) < 0.01f,
                    "Koki return did not restore its body and core.");
                Require(kokiBody.Transform.IsEqualApprox(authoredBody), "Koki return inverted the authored body transform.");
            }
            koki.Free();
            GD.Print("PASS Koki actual summon tilt: per-frame mirrored facing, contour grounding, core/origin tracking and exact transform return.");
            await VerifySawatariHurt(combat, stage);
        }
        finally
        {
            Invoke("Reset");
            actor.Free();
            target.Free();
            stage.Free();
            spinViewport?.Free();
            AimActors.Clear();
            harmony.UnpatchAll(harmony.Id);
        }
    }

    private static bool ResolveAimActor(Creature __instance, ref NCreature? __result)
    {
        if (!AimActors.TryGetValue(__instance, out NCreature? actor)) return true;
        __result = actor;
        return false;
    }
}

public partial class AimContractCreature : NCreature
{
    public override void _Ready() { }
    public override void _EnterTree() { }
    public override void _ExitTree() { }
    public override void _Process(double delta) { }
}

public partial class AimContractVisuals : NCreatureVisuals
{
    public override void _Ready() { }
    public override void _EnterTree() { }
    public override void _ExitTree() { }
    public override void _Process(double delta) { }
}
