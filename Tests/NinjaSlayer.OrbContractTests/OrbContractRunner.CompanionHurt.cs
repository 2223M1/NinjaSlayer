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
                        try
                        {
                            var hitFx = AccessTools.Method(feedback, "PlaySawatariBambooHit");
                            hitFx.Invoke(null, [creature, combat.Player.Creature, false]);
                            Require(effects.GetChildCount() == 0, "An evaded bamboo stab generated contact sparks.");
                            hitFx.Invoke(null, [creature, combat.Player.Creature, true]);
                            var impact = effects.GetNode<Node2D>("SawatariBambooImpact");
                            Require(impact.GetChildCount() == 2 && impact.Scale.IsEqualApprox(Vector2.One * .35f),
                                "Bamboo retained the dramatic stab's long slash or incorrect scale.");
                            Require(impact.GetNode<GpuParticles2D>("Sparks").ProcessMaterial != sourceMaterial
                                && sparks.Amount == sourceAmount, "Bamboo changed shared native particle resources.");
                            await ToSignal(GetTree().CreateTimer(.2), SceneTreeTimer.SignalName.Timeout);
                            Require(effects.GetChildCount() == 0, "Bamboo contact particles did not clean up.");
                        }
                        finally { original.Free(); }
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
                NinjaSlayerAnimationPatch.Prefix(dark, "Hit", 0f, ref hit);
                Task wait = (Task)AccessTools.Method(typeof(StaggerAnimation), "WaitForCompletion").Invoke(null, [dark])!;
                Require(!wait.IsCompleted, "Dark Ninja counter did not wait for its active hurt.");
                await wait;
                Require(darkRig.GetNode<Node2D>("AirborneAnchor").Transform.IsEqualApprox(Transform2D.Identity),
                    "Dark Ninja hurt did not fully return before counter preparation.");
                ulong start = Time.GetTicksUsec();
                NinjaSlayerAnimationPatch.Prefix(dark, "SlowAttack", .5f, ref hit);
                await hit;
                Require(Time.GetTicksUsec() - start >= 490000 && Math.Abs(darkRig.Position.X + 90f) < 1f,
                    "Dark Ninja counter did not use the reference Slow gate and lunge.");
                await ToSignal(GetTree().CreateTimer(.52), SceneTreeTimer.SignalName.Timeout);
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
            GD.Print("PASS Dark Ninja actual Hit -> completed return -> reference Slow; native bamboo contact resources and cleanup.");
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

    private async Task VerifyReferenceAndBamboo(Creature creature, NCreature actor, NCreatureVisuals rig, Node2D anchor)
    {
        FastModeType original = SaveManager.Instance.PrefsSave.FastMode;
        Vector2 root = actor.Position;
        Vector2 baseline = rig.Position;
        Vector2 core = rig.VfxSpawnPosition.Position;
        Transform2D transform = anchor.Transform;
        float direction = creature.Side == CombatSide.Player ? 1f : -1f;
        var product = typeof(StaggerAnimation).Assembly;
        Type bamboo = product.GetType("NinjaSlayer.Code.ExternalAnimations.SawatariBambooAnimation", true)!;
        try
        {
            foreach (FastModeType mode in new[] { FastModeType.Normal, FastModeType.Fast, FastModeType.Instant })
            {
                SaveManager.Instance.PrefsSave.FastMode = mode;
                ulong start = Time.GetTicksUsec();
                await (Task)AccessTools.Method(typeof(SlowAttackAnimation), "PlayReference").Invoke(null, [creature])!;
                if (mode != FastModeType.Instant)
                {
                    double elapsed = (Time.GetTicksUsec() - start) / 1000000d;
                    Require(elapsed >= .49 && elapsed < .55, $"Reference Slow gate was {elapsed:F4}s in {mode}.");
                    Require(Math.Abs((rig.Position.X - baseline.X) * direction - 90f) < 1f,
                        $"Reference Slow released damage away from the 90px peak: {rig.Position - baseline}.");
                    await ToSignal(GetTree().CreateTimer(.52), SceneTreeTimer.SignalName.Timeout);
                }
                Require(actor.Position.IsEqualApprox(root) && rig.Position.IsEqualApprox(baseline),
                    "Reference Slow moved the UI root or did not restore its visual baseline.");
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
                Require(anchor.Transform.IsEqualApprox(transform) && rig.VfxSpawnPosition.Position.IsEqualApprox(core),
                    "Bamboo did not restore its body/core transforms.");
                GD.Print($"PASS {creature.Side} bamboo {mode} hit timestamps: {string.Join(", ", impacts.Select(t => t.ToString("F4")))}.");
            }
            GD.Print($"PASS actual {creature.Side} reference Slow and bamboo: Normal/Fast/Instant, 90px peak, six-frame cadence, UI stability and recovery.");
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
