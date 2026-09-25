using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
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
        var effects = new Control();
        stage.AddChild(effects);
        AccessTools.Property(typeof(NCombatRoom), nameof(NCombatRoom.CombatVfxContainer)).SetValue(_hurtRoom, effects);
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
                // This fixture bypasses AfterAddedToRoom; initialize its normal body facing too.
                AccessTools.Method(typeof(SawatariMonster), "SetFacingPlayerSide")
                    .Invoke(creature.Monster, [side == CombatSide.Player]);
                Node2D anchor = rig.GetNode<Node2D>("AirborneAnchor");
                Transform2D baseline = anchor.Transform;
                Vector2 rootBaseline = actor.Position;
                try
                {
                    Task hit = Task.CompletedTask;
                    Require(!NinjaSlayerAnimationPatch.Prefix(creature, "Hit", 0f, ref hit, out _),
                        "Sawatari Hit was not handled by the mod animation route.");
                    if (side == CombatSide.Enemy)
                    {
                        Require(hit.IsCompletedSuccessfully && StaggerAnimation.IsActive(creature),
                            "Enemy Sawatari must start a nonblocking shared stagger.");
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                        Require(actor.Position.IsEqualApprox(rootBaseline) && rig.VfxSpawnPosition.Position.X > 0f && anchor.RotationDegrees > 0f,
                            "Enemy Sawatari did not recoil and lean away from the player.");
                        var resume = (Action)AccessTools.Method(typeof(StaggerAnimation), "PauseCurrent").Invoke(null, [creature])!;
                        Transform2D frozen = anchor.Transform;
                        Task completion = (Task)AccessTools.Method(typeof(StaggerAnimation), "WaitForCompletion").Invoke(null, [creature])!;
                        await ToSignal(GetTree().CreateTimer(.05), SceneTreeTimer.SignalName.Timeout);
                        Require(!completion.IsCompleted && anchor.Transform.IsEqualApprox(frozen),
                            "Paused hurt was treated as cancelled and restored before the counter could wait for it.");
                        resume();
                        await (Task)AccessTools.Method(typeof(StaggerAnimation), "WaitForCompletion").Invoke(null, [creature])!;
                        Require(actor.Position.IsEqualApprox(rootBaseline) && anchor.Transform.IsEqualApprox(baseline),
                            "Enemy Sawatari did not restore after hurt.");
                        for (int hitIndex = 0; hitIndex < 2; hitIndex++)
                        {
                            NinjaSlayerAnimationPatch.Prefix(creature, "Hit", 0f, ref hit, out _);
                            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                        }
                        StaggerAnimation.Reset(creature);
                        Require(actor.Position.IsEqualApprox(rootBaseline) && anchor.Transform.IsEqualApprox(baseline),
                            "Repeated/interrupted hurt left Sawatari displaced.");
                        NinjaSlayerAnimationPatch.Prefix(creature, "BlockedHit", .05f, ref hit, out _);
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
                    await VerifyReferenceAndBamboo(creature, actor, rig, anchor);
                    if (side == CombatSide.Enemy)
                    {
                        Type feedback = typeof(StaggerAnimation).Assembly.GetType("NinjaSlayer.Content.NinjaSlayerCombatVfx", true)!;
                        const string stabPath = "res://scenes/vfx/vfx_dramatic_stab.tscn";
                        RegisterMountedUids(stabPath, []);
                        var scene = GD.Load<PackedScene>(stabPath);
                        Node2D original = scene.Instantiate<Node2D>();
                        var sparks = original.GetNode<GpuParticles2D>("Sparks");
                        Material sourceMaterial = sparks.ProcessMaterial;
                        int sourceAmount = sparks.Amount;
                        bool testMode = MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn;
                        PreloadManager.Cache.SetAsset(stabPath, scene);
                        try
                        {
                            MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn = false;
                            var hitFx = AccessTools.Method(feedback, "PlaySawatariBambooHit");
                            hitFx.Invoke(null, [combat.Player.Creature, false]);
                            Require(effects.GetChildCount() == 0, "An evaded bamboo stab generated contact sparks.");
                            hitFx.Invoke(null, [combat.Player.Creature, true]);
                            var impact = effects.GetChildren().OfType<Node2D>().Single();
                            Require(!impact.HasNode("slash") && impact.HasNode("Flash") && original.HasNode("slash")
                                && impact.GetChildCount() == original.GetChildCount() - 1 && impact.Scale.IsEqualApprox(original.Scale)
                                && impact.Rotation == original.Rotation,
                                "Bamboo must remove only its own line, preserving native size and cached scene.");
                            Require(impact.GetNode<GpuParticles2D>("Sparks").Amount == sourceAmount
                                && impact.GetNode<GpuParticles2D>("Sparks").ProcessMaterial == sourceMaterial
                                && impact.GetNode<GpuParticles2D>("Flash").ProcessMaterial == original.GetNode<GpuParticles2D>("Flash").ProcessMaterial,
                                "Bamboo changed native particle parameters.");
                            Require(impact.GlobalPosition.DistanceTo(combat.Player.Creature.GetCreatureNode()!.VfxSpawnPosition) < .1f,
                                "Bamboo impact did not use the target's native VFX core.");
                            await ToSignal(GetTree().CreateTimer(2.5), SceneTreeTimer.SignalName.Timeout);
                            Require(effects.GetChildCount() == 0, "Bamboo contact particles did not clean up.");
                            await VerifySawatariFlyingSlash(creature, combat.Player.Creature, actor, effects);
                        }
                        finally { MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn = testMode; original.Free(); }
                    }
                }
                finally
                {
                    StaggerAnimation.Reset(creature);
                    AimActors.Remove(creature);
                    actor.Free();
                }
            }
            const string darkPath = "res://NinjaSlayer/scenes/creature_visuals/dark_ninja.tscn";
            PreloadManager.Cache.SetAsset(darkPath, GD.Load(darkPath));
            var dark = new Creature(ModelDb.Monster<DarkNinjaMonster>().ToMutable(), CombatSide.Enemy, null)
                { CombatState = combat.State };
            dark.SetMaxHpInternal(100);
            dark.SetCurrentHpInternal(100);
            var darkActor = new AimContractCreature();
            NCreatureVisuals darkRig = STS2RitsuLib.Scaffolding.Godot.RitsuGodotNodeFactories
                .CreateFromScenePath<NCreatureVisuals>(darkPath)!;
            AccessTools.Property(typeof(NCreature), "Entity").SetValue(darkActor, dark);
            AccessTools.Property(typeof(NCreature), "Visuals").SetValue(darkActor, darkRig);
            darkActor.AddChild(darkRig);
            stage.AddChild(darkActor);
            AimActors.Add(dark, darkActor);
            try
            {
                Task hit = Task.CompletedTask;
                NinjaSlayerAnimationPatch.Prefix(dark, "Hit", 0f, ref hit, out _);
                Task wait = (Task)AccessTools.Method(typeof(StaggerAnimation), "WaitForCompletion").Invoke(null, [dark])!;
                Require(!wait.IsCompleted, "Dark Ninja counter did not wait for its active hurt.");
                await wait;
                Require(darkRig.GetNode<Node2D>("AirborneAnchor").Transform.IsEqualApprox(Transform2D.Identity),
                    "Dark Ninja hurt did not fully return before counter preparation.");
                ulong start = Time.GetTicksUsec();
                NinjaSlayerAnimationPatch.Prefix(dark, "SlowAttack", .5f, ref hit, out _);
                await hit;
                double frameSeconds = Math.Max(1d / 60d, GetProcessDeltaTime());
                double gate = SaveManager.Instance.PrefsSave.FastMode == FastModeType.Normal ? .5d : .25d;
                Require((Time.GetTicksUsec() - start) / 1000000d + frameSeconds >= gate
                    && Math.Abs(darkRig.Position.X + 120f) < 1f,
                    "Dark Ninja counter did not use the slash gate and 120px lunge.");
                await ToSignal(GetTree().CreateTimer(.27), SceneTreeTimer.SignalName.Timeout);
                Require(darkRig.Position.IsZeroApprox() && darkActor.Position.IsZeroApprox(),
                    "Dark Ninja counter did not restore its body without moving the UI root.");
            }
            finally
            {
                AccessTools.Method(typeof(StaggerAnimation).Assembly.GetType("NinjaSlayer.Code.ExternalAnimations.NinjaSlayerRapidAnimationCoordinator", true)!,
                    "CancelAndRestore").Invoke(null, [dark]);
                AimActors.Remove(dark);
                darkActor.Free();
            }
            GD.Print("PASS Dark Ninja actual Hit -> completed return -> Iai; native bamboo contact resources and cleanup.");
            GD.Print("PASS Sawatari actual hurt: enemy shared stagger, repeated-hit cleanup, blocked shake and unchanged allied dodge.");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            effects.Free();
            _hurtRoom.Free();
            _hurtRoom = null;
        }
    }

    private async Task VerifySawatariFlyingSlash(Creature source, Creature target, NCreature actor, Control effects)
    {
        GDExtensionManager.LoadExtension(System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath(
            "res://../../addons/spine/spine_godot_extension.gdextension")));
        const string path = "res://scenes/vfx/vfx_flying_slash.tscn";
        RegisterMountedUids(path, []);
        var scene = GD.Load<PackedScene>(path);
        PreloadManager.Cache.SetAsset(path, scene);
        var play = AccessTools.Method(typeof(NinjaSlayer.Content.NinjaSlayerCombatVfx), "PlaySawatariFlyingSlash");
        Vector2 baseline = actor.Position;
        Transform2D parentBaseline = effects.GetTransform();
        try
        {
            effects.Position = new(31, -17);
            effects.Scale = new(.8f, 1.2f);
            foreach (Vector2 offset in new Vector2[] { new(450, 90), new(-350, -120), new(100, 0) })
            {
                actor.Position = baseline + offset;
                Vector2 start = actor.VfxSpawnPosition;
                Vector2 end = target.GetCreatureNode()!.VfxSpawnPosition;
                play.Invoke(null, [source, target]);
                var effect = effects.GetChildren().OfType<Node2D>().Single();
                effect.ProcessMode = ProcessModeEnum.Disabled;
                var reference = scene.Instantiate<Node2D>();
                AddChild(reference);
                reference.ProcessMode = ProcessModeEnum.Disabled;
                Node sprite = effect.GetNode("SpineSprite");
                Node native = reference.GetNode("SpineSprite");
                native.Call("update_skeleton", 0f);
                string[] names = ["Small", "mid", "large"];
                GodotObject Bone(Node node, string name) => node.Call("get_skeleton").AsGodotObject().Call("find_bone", name).AsGodotObject();
                float[] starts = names.Select(name => Bone(native, name).Call("get_x").AsSingle()).ToArray();
                Vector2 direction = (end - start).Normalized();
                float last = -1;
                for (int frame = 0; frame <= 9; frame++)
                {
                    for (int i = 0; i < names.Length; i++)
                    {
                        GodotObject bone = Bone(sprite, names[i]);
                        GodotObject original = Bone(native, names[i]);
                        Vector2 point = ((Node2D)sprite).ToGlobal(new(bone.Call("get_world_x").AsSingle(), -bone.Call("get_world_y").AsSingle()));
                        float progress = 1f - original.Call("get_x").AsSingle() / starts[i];
                        Require(point.DistanceTo(start.Lerp(end, progress)) < .5f,
                            $"Flying slash {names[i]} missed the attacker-to-target route at frame {frame}: {point}.");
                        Require(Mathf.IsEqualApprox(bone.Call("get_scale_x").AsSingle(), original.Call("get_scale_x").AsSingle())
                            && Mathf.IsEqualApprox(bone.Call("get_scale_y").AsSingle(), original.Call("get_scale_y").AsSingle())
                            && Mathf.IsEqualApprox(effect.GlobalTransform.X.Length(), 1f)
                            && Mathf.IsEqualApprox(effect.GlobalTransform.Y.Length(), 1f),
                            "Flying slash changed native artwork dimensions.");
                        if (i == 2)
                        {
                            float along = (point - start).Dot(direction);
                            Require(along + .5f >= last, "Flying slash moved away from its target.");
                            last = along;
                        }
                    }
                    sprite.Call("update_skeleton", 1f / 30f);
                    native.Call("update_skeleton", 1f / 30f);
                }
                reference.Free();
                effect.ProcessMode = ProcessModeEnum.Inherit;
                await ToSignal(GetTree().CreateTimer(.5), SceneTreeTimer.SignalName.Timeout);
                Require(effects.GetChildCount() == 0, "Flying slash lost native animation completion cleanup.");
            }
            GD.Print("PASS Sawatari Iron Wave actual Spine route: both directions, high/low targets, native size and cleanup.");
        }
        finally { actor.Position = baseline; effects.Position = parentBaseline.Origin; effects.Scale = parentBaseline.Scale; }
    }

    private async Task VerifyReferenceAndBamboo(Creature creature, NCreature actor, NCreatureVisuals rig, Node2D anchor)
    {
        FastModeType original = SaveManager.Instance.PrefsSave.FastMode;
        Vector2 root = actor.Position;
        Vector2 baseline = rig.Position;
        Vector2 core = rig.VfxSpawnPosition.Position;
        Transform2D transform = anchor.Transform;
        Sprite2D bambooBody = rig.GetNode<Sprite2D>("%Visuals");
        Transform2D bodyTransform = bambooBody.Transform;
        float direction = creature.Side == CombatSide.Player ? 1f : -1f;
        var product = typeof(StaggerAnimation).Assembly;
        Type bamboo = product.GetType("NinjaSlayer.Code.ExternalAnimations.SawatariBambooAnimation", true)!;
        try
        {
            foreach (FastModeType mode in new[] { FastModeType.Normal, FastModeType.Fast, FastModeType.Instant })
            {
                SaveManager.Instance.PrefsSave.FastMode = mode;
                ulong start = Time.GetTicksUsec();
                await (Task)AccessTools.Method(typeof(SlowAttackAnimation), "PlayIai").Invoke(null, [creature])!;
                if (mode != FastModeType.Instant)
                {
                    double elapsed = (Time.GetTicksUsec() - start) / 1000000d;
                    // A new Tween consumes the current frame's delta, even when created mid-frame.
                    double frameSeconds = Math.Max(1d / 60d, GetProcessDeltaTime());
                    double gate = mode == FastModeType.Normal ? .5d : .25d;
                    Require(elapsed + frameSeconds >= gate && elapsed < gate + 2d * frameSeconds,
                        $"Iai gate was {elapsed:F4}s in {mode} (frame {frameSeconds:F4}s).");
                    Require(Math.Abs((rig.Position.X - baseline.X) * direction - 120f) < 1f,
                        $"Iai released damage away from the 120px peak: {rig.Position - baseline}.");
                    await ToSignal(GetTree().CreateTimer(.27), SceneTreeTimer.SignalName.Timeout);
                }
                Require(actor.Position.IsEqualApprox(root) && rig.Position.IsEqualApprox(baseline),
                    "Iai moved the UI root or did not restore its visual baseline.");
                var impacts = new List<double>();
                start = Time.GetTicksUsec();
                Task Impact()
                {
                    impacts.Add((Time.GetTicksUsec() - start) / 1000000d);
                    Require(actor.Position.IsEqualApprox(root), "Bamboo moved the UI root.");
                    if (mode != FastModeType.Instant)
                    {
                        Require(Math.Abs((rig.VfxSpawnPosition.Position.X - core.X) * direction - 126f) < .5f
                            && Math.Abs(anchor.RotationDegrees * direction - 19.49f) < .1f,
                            "Bamboo damage did not occur at the source-reference peak.");
                        Require(bambooBody.Transform.IsEqualApprox(bodyTransform) && bambooBody.VisibilityLayer != 0
                            && !anchor.HasNode("BambooPose"),
                            "Bamboo must retain one complete source image with no relative body/weapon motion.");
                    }
                    return Task.CompletedTask;
                }
                await (Task)AccessTools.Method(bamboo, "Play").Invoke(null, [creature, 4, (Func<Task>)Impact])!;
                Require(impacts.Count == 4, "Bamboo dropped or duplicated a hit.");
                if (mode != FastModeType.Instant)
                {
                    for (int i = 0; i < impacts.Count; i++)
                        Require(Math.Abs(impacts[i] - (4d / 6d + i) * .25025d) < .035,
                            $"Bamboo cadence drifted in {mode}: {string.Join(", ", impacts.Select(t => t.ToString("F4")))}.");
                }
                if (creature.Side == CombatSide.Player && creature.PetOwner != null && mode != FastModeType.Instant)
                {
                    Require(!anchor.Transform.IsEqualApprox(transform), "Friendly bamboo still waited for its final return.");
                    Require((Time.GetTicksUsec() - start) / 1000000d - impacts[^1] < .035,
                        "Friendly bamboo delayed release after its final impact.");
                    await ToSignal(GetTree().CreateTimer(.15), SceneTreeTimer.SignalName.Timeout);
                }
                Require(anchor.Transform.IsEqualApprox(transform) && rig.VfxSpawnPosition.Position.IsEqualApprox(core),
                    "Bamboo did not restore its body/core transforms.");
                GD.Print($"PASS {creature.Side} bamboo {mode} hit timestamps: {string.Join(", ", impacts.Select(t => t.ToString("F4")))}.");
            }
            foreach (FastModeType mode in new[] { FastModeType.Normal, FastModeType.Fast })
            {
                SaveManager.Instance.PrefsSave.FastMode = mode;
                ulong hurtStart = Time.GetTicksUsec();
                Task hurt = StaggerAnimation.Play(creature);
                await ToSignal(GetTree().CreateTimer(.18), SceneTreeTimer.SignalName.Timeout);
                Require(!hurt.IsCompleted, $"{creature.Side} hurt ended before the reference 0.3s in {mode}.");
                await hurt;
                Require((Time.GetTicksUsec() - hurtStart) / 1000000d
                    + Math.Max(1d / 60d, GetProcessDeltaTime()) >= .3d, "Hurt was shortened by game speed.");
                foreach (bool hurtFirst in new[] { false, true })
                foreach (int hits in new[] { 1, 2 })
                {
                    Task first = hurtFirst ? StaggerAnimation.Play(creature)
                        : (Task)AccessTools.Method(bamboo, "Play").Invoke(null, [creature, hits, (Func<Task>)(() => Task.CompletedTask)])!;
                    await ToSignal(GetTree().CreateTimer(.035), SceneTreeTimer.SignalName.Timeout);
                    Task second = hurtFirst
                        ? (Task)AccessTools.Method(bamboo, "Play").Invoke(null, [creature, hits, (Func<Task>)(() => Task.CompletedTask)])!
                        : StaggerAnimation.Play(creature);
                    await Task.WhenAll(first, second);
                    if (creature.Side == CombatSide.Player && creature.PetOwner != null)
                        await ToSignal(GetTree().CreateTimer(.15), SceneTreeTimer.SignalName.Timeout);
                    Require(actor.Position.IsEqualApprox(root) && rig.Position.IsEqualApprox(baseline)
                        && anchor.Transform.IsEqualApprox(transform) && rig.VfxSpawnPosition.Position.IsEqualApprox(core),
                        $"Overlapping bamboo/hurt captured a tilted baseline: {creature.Side}, {mode}, hurtFirst={hurtFirst}, hits={hits}.");
                }
            }
            GD.Print($"PASS {creature.Side} bamboo/hurt overlap: both start orders, both completion orders, Normal/Fast and exact baseline recovery.");
            GD.Print($"PASS actual {creature.Side} standard Slow and bamboo: Normal/Fast/Instant, 120px peak, six-frame cadence, UI stability and recovery.");
        }
        finally
        {
            SaveManager.Instance.PrefsSave.FastMode = original;
            AccessTools.Method(product.GetType("NinjaSlayer.Code.ExternalAnimations.NinjaSlayerRapidAnimationCoordinator", true)!,
                "CancelAndRestore").Invoke(null, [creature]);
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
