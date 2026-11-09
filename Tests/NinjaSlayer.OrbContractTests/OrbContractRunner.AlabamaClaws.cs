using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx.Backgrounds;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Orbs;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private async Task VerifyAlabamaClaws(NKaiserCrabBossBackground crab, Creature owner,
        Creature crusher, Creature rocket, Control scene)
    {
        var spine = crab.GetNode<Node2D>("%Visuals");
        var sprite = new MegaSprite(spine);
        var state = sprite.GetAnimationState();
        using var stateLease = state.BoundObject;
        stateLease.Call("set_time_scale", 0f);
        spine.Call("update_skeleton", 0f);
        var actors = (List<NCreature>)AccessTools.Field(typeof(NCombatRoom), "_creatureNodes").GetValue(_hurtRoom)!;
        var created = new List<NCreature>();
        var harmony = new Harmony("NinjaSlayer.OrbContracts.AlabamaClaws");
        harmony.Patch(AccessTools.Method(typeof(Creature), nameof(Creature.GetCreatureNode)),
            prefix: new HarmonyMethod(GetType(), nameof(ResolveAimActor)));
        NCreature AddActor(Creature creature, string? nativeScene, Vector2 position)
        {
            NCreatureVisuals visuals;
            if (nativeScene != null)
            {
                RegisterMountedUids(nativeScene, []);
                visuals = GD.Load<PackedScene>(nativeScene).Instantiate<NCreatureVisuals>();
            }
            else
            {
                visuals = new AimContractVisuals();
                var drawing = new Sprite2D { Name = "Visuals", Position = new(0, -160),
                    Texture = GD.Load<Texture2D>("res://NinjaSlayer/images/monsters/dark_ninja.png"), Scale = new(.35f, .35f) };
                var bounds = new Control { Position = new(-65, -320), Size = new(130, 320) };
                var center = new Marker2D { Position = new(0, -160) };
                visuals.AddChild(drawing); visuals.AddChild(bounds); visuals.AddChild(center);
                drawing.Owner = visuals; drawing.UniqueNameInOwner = true;
                AccessTools.Field(typeof(NCreatureVisuals), "_body").SetValue(visuals, drawing);
                AccessTools.Property(typeof(NCreatureVisuals), "Bounds").SetValue(visuals, bounds);
                AccessTools.Property(typeof(NCreatureVisuals), "VfxSpawnPosition").SetValue(visuals, center);
            }
            var actor = new AimContractCreature { Position = position };
            AccessTools.Property(typeof(NCreature), "Entity").SetValue(actor, creature);
            AccessTools.Property(typeof(NCreature), "Visuals").SetValue(actor, visuals);
            actor.AddChild(visuals); scene.AddChild(actor); actors.Add(actor);
            AimActors.Add(creature, actor); created.Add(actor);
            return actor;
        }
        var player = AddActor(owner, null, new(200, 700));
        var left = AddActor(crusher, "res://scenes/creature_visuals/crusher.tscn", new(700, 700));
        var right = AddActor(rocket, "res://scenes/creature_visuals/rocket.tscn", new(1200, 700));
        AccessTools.Property(typeof(NCombatRoom), "BackCombatVfxContainer").SetValue(_hurtRoom, scene);
        Transform2D spineBefore = spine.Transform;
        var leftArm = crab.GetNode<Node2D>("%ArmBoneL");
        var rightArm = crab.GetNode<Node2D>("%ArmBoneR");
        var leftBefore = leftArm.Transform; var rightBefore = rightArm.Transform;
        Type motionType = typeof(ShurikenOrb).Assembly.GetType("NinjaSlayer.Code.ExternalAnimations.AlabamaClawMotion", true)!;
        static bool SameBone(Transform2D a, Transform2D b) => a.Origin.DistanceTo(b.Origin) < .01f
            && a.X.DistanceTo(b.X) < .0001f && a.Y.DistanceTo(b.Y) < .0001f;
        try
        {
            foreach (var (victim, arm, bone, otherBone) in new[]
            {
                (left, leftArm, "claw_l_offset", "claw_r_offset"),
                (right, rightArm, "claw_r_offset", "claw_l_offset")
            })
            {
                Transform2D targetBefore = victim.Body.Transform;
                Transform2D visualBefore = victim.Visuals.Transform;
                Transform2D boneBefore = spine.Call("get_global_bone_transform", bone).AsTransform2D();
                Transform2D otherBefore = spine.Call("get_global_bone_transform", otherBone).AsTransform2D();
                int impacts = 0;
                float rise = 0;
                bool inverted = false;
                var play = AlabamaDropAnimation.Play(owner, victim.Entity, () => { impacts++; return Task.CompletedTask; });
                for (int frame = 0; frame < 500 && !play.IsCompleted; frame++)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    spine.Call("update_skeleton", 0f);
                    Transform2D current = spine.Call("get_global_bone_transform", bone).AsTransform2D();
                    rise = Math.Max(rise, boneBefore.Origin.Y - current.Origin.Y);
                    inverted |= current.Y.Dot(boneBefore.Y) < 0;
                    Require(SameBone(spine.Call("get_global_bone_transform", otherBone).AsTransform2D(), otherBefore),
                        "Alabama moved the unselected native claw.");
                    Require(spine.Transform.IsEqualApprox(spineBefore), "Alabama moved the shared crab body.");
                }
                Require(play.IsCompleted, "Alabama claw choreography did not finish.");
                await play;
                spine.Call("update_skeleton", 0f);
                Require(impacts == 1 && rise > 700f && inverted,
                    $"Alabama must lift and invert the actual {bone}, then impact once (rise={rise}, inverted={inverted}, impacts={impacts}).");
                Require(victim.Body.Transform.IsEqualApprox(targetBefore) && victim.Visuals.Transform.IsEqualApprox(visualBefore)
                    && SameBone(spine.Call("get_global_bone_transform", bone).AsTransform2D(), boneBefore),
                    $"Alabama did not restore {bone}: body {targetBefore} => {victim.Body.Transform}; visuals {visualBefore} => {victim.Visuals.Transform}; bone {boneBefore} => {spine.Call("get_global_bone_transform", bone).AsTransform2D()}.");

                var deathPlay = AlabamaDropAnimation.Play(owner, victim.Entity, () =>
                {
                    AccessTools.Method(typeof(AlabamaDropAnimation), "ReleaseVictimBeforeDeath").Invoke(null, [victim]);
                    Require(leftArm.Transform.IsEqualApprox(leftBefore) && rightArm.Transform.IsEqualApprox(rightBefore),
                        "Alabama must release native claw overrides before death capture.");
                    victim.Entity.SetCurrentHpInternal(0);
                    crab.PlayArmDeathAnim(victim == left ? NKaiserCrabBossBackground.ArmSide.Left : NKaiserCrabBossBackground.ArmSide.Right);
                    return Task.CompletedTask;
                });
                for (int frame = 0; frame < 500 && !deathPlay.IsCompleted; frame++)
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Require(deathPlay.IsCompleted, "Alabama claw death did not finish.");
                await deathPlay;
                Require(leftArm.Transform.IsEqualApprox(leftBefore) && rightArm.Transform.IsEqualApprox(rightBefore),
                    "Late Alabama recovery overwrote the native death pose.");
                victim.Entity.SetCurrentHpInternal(100);
                victim.Body.Transform = targetBefore; victim.Visuals.Transform = visualBefore;
                state.SetAnimation(victim == left ? "left/idle_loop" : "right/idle_loop", true, victim == left ? 1 : 2);
                using (var track = state.GetCurrent(victim == left ? 1 : 2)!.BoundObject) track.Call("set_mix_duration", 0f);
                stateLease.Call("clear_track", 3);
                spine.Call("update_skeleton", 0f);
            }
            if (System.Environment.GetEnvironmentVariable("NINJASLAYER_FEEDBACK_RENDER_DIR") is { } directory)
            {
                var viewport = new SubViewport { Size = new(1600, 1000), TransparentBg = true,
                    RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
                AddChild(viewport);
                Node parent = _hurtRoom!.GetParent();
                _hurtRoom.Reparent(viewport);
                using var skeleton = sprite.GetSkeleton()!.BoundObject;
                Rect2 bounds = spine.GlobalTransform * skeleton.Call("get_bounds").AsRect2();
                float scale = Math.Min(1400 / bounds.Size.X, 550 / bounds.Size.Y);
                viewport.CanvasTransform = new Transform2D(0f, Vector2.One * scale, 0f,
                    new Vector2(800, 650) - bounds.GetCenter() * scale);
                async Task Capture(string name)
                {
                    spine.Call("update_skeleton", 0f);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var pixels = viewport.GetTexture().GetImage();
                    Require(pixels.GetUsedRect().HasArea(), "Alabama claw render has no pixels.");
                    System.IO.Directory.CreateDirectory(directory);
                    Require(pixels.SavePng(System.IO.Path.Combine(directory, name + ".png")) == Error.Ok, "Could not save claw render.");
                }
                await Capture("alabama-before");
                foreach (var victim in new[] { left, right })
                {
                    using var motion = (IDisposable)Activator.CreateInstance(motionType,
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, [victim, _hurtRoom], null)!;
                    victim.Visuals.Position += new Vector2(0, -600);
                    await Capture(victim == left ? "alabama-left-lift" : "alabama-right-lift");
                    victim.Visuals.Position -= new Vector2(0, -600);
                }
                _hurtRoom.Reparent(parent);
                viewport.Free();
            }
            using (var motion = (IDisposable)Activator.CreateInstance(motionType,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, [left, _hurtRoom], null)!)
            {
                left.Visuals.Position += new Vector2(120, -300);
                spine.Call("update_skeleton", 0f);
                Require(!leftArm.Transform.IsEqualApprox(leftBefore), "Native claw node did not follow motion.");
                scene.RemoveChild(left);
                Require(leftArm.Transform.IsEqualApprox(leftBefore), "Exiting the room left the claw displaced.");
            }
            GD.Print("PASS Alabama real left/right claws: rise, inversion, single impact, independent body/other claw, survival restore, death handoff and tree-exit cleanup.");
        }
        finally
        {
            stateLease.Call("set_time_scale", 1f);
            foreach (var actor in created) { actors.Remove(actor); AimActors.Remove(actor.Entity); actor.Free(); }
            harmony.UnpatchAll(harmony.Id);
        }
    }
}
