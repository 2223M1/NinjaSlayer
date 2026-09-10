using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Cards.RedesignV1;
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
        AddChild(stage);
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
            Invoke("BeginTornado", combat.Enemy, false);
            Invoke("SetTravel", Vector2.Zero, new Vector2(120f, 0f), 1f);
            Invoke("SyncNow");
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
                Invoke("BeginAction", combat.Enemy, true, false);
                Invoke("PlaceAtImpact", combat.Enemy, 600f);
                Require(actor.Position.X == 600f, "Finisher no longer places the actor directly at impact.");
                Vector2 aimedForward = pose.GetGlobalTransformWithCanvas().BasisXform(Vector2.Right).Normalized();
                Vector2 aimedTarget = (targetCenter.GlobalPosition - center.GlobalPosition).Normalized();
                Require(aimedForward.Dot(aimedTarget) > 0.999f, "Finisher impact pose lost its dynamic incoming direction.");
                Invoke("BeginReturn");
                Invoke("ApplyReturn", 1f);
            }
            actor.Position = Vector2.Zero;
            anchor.Position = Vector2.Zero;
            await VerifyNativeDrawBatches(combat, pose);
            await VerifyNonblockingThrow(combat, pose);
            await VerifyConcurrentMotion(combat, pose, anchor, center, target);
            var dragOwner = new Node();
            stage.AddChild(dragOwner);
            var tornadoCard = combat.State.CreateCard<TornadoFistRedesignV1>(combat.Player);
            Invoke("Drag", dragOwner, tornadoCard, new Vector2(700f, -300f), combat.Enemy);
            pose._Process(0.3);
            Vector2 chargedTravel = (Vector2)AccessTools.Property(poseType, "Travel").GetValue(pose)!;
            Require(Math.Abs(chargedTravel.X + 24f) < 0.01f, "Tornado drag did not stop at its 24px backstep.");
            Invoke("EndDrag", dragOwner, false);
            await ToSignal(GetTree().CreateTimer(0.25f), SceneTreeTimer.SignalName.Timeout);
            Require(pose.Transform.IsEqualApprox(Transform2D.Identity), "Cancelled drag left its charge pose active.");
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
                    Invoke("BeginReturn");
                    Invoke("ApplyReturn", 1f);
                }
                Sprite2D formOverlay = rig.GetNode<Sprite2D>("AirborneAnchor/AimPose/NarakuVisualOverlay");
                VerifyDrawAndThrowPose(combat, pose, anchor, formOverlay.Visible ? formOverlay : sprite, center, target, formIndex);
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
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
            foreach (float facing in new[] { -1f, 1f })
            {
                kokiBody.Scale = new(facing, 1f);
                Vector2 coreLocal = kokiBody.ToLocal(kokiCenter.GlobalPosition);
                float ground = kokiContour.Max(p => (kokiSprite.GetGlobalTransformWithCanvas() * new Vector2(p.X, p.Y)).Y);
                await (Task)tilt.Invoke(null, [kokiBody, kokiCenter, centerBaseline, bodyBaseline, 0f, 0f, 15f, 0.1f])!;
                float bottom = kokiContour.Max(p => (kokiSprite.GetGlobalTransformWithCanvas() * new Vector2(p.X, p.Y)).Y);
                Require(Math.Abs(bottom - ground) < 0.1f, "Koki contour left its ground line.");
                Require(kokiBody.ToGlobal(coreLocal).DistanceTo(kokiCenter.GlobalPosition) < 0.1f, "Koki summon origin did not follow its pivot.");
                await (Task)tilt.Invoke(null, [kokiBody, kokiCenter, centerBaseline, bodyBaseline, 0f, 15f, 0f, 0.2f])!;
                Require(kokiBody.Position.DistanceTo(bodyBaseline) < 0.01f && kokiCenter.Position.DistanceTo(centerBaseline) < 0.01f,
                    "Koki return did not restore its body and core.");
            }
            koki.Free();
            GD.Print("PASS Koki actual summon tilt: both facings, contour grounding, core/origin tracking and exact return.");
        }
        finally
        {
            Invoke("Reset");
            actor.Free();
            target.Free();
            stage.Free();
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
