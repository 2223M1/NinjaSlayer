using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Orbs;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private async Task VerifyIndependentPresentation(OrbCombat combat, Node2D pose, Marker2D center)
    {
        void Call(string name, params object?[] args) => AccessTools.Method(pose.GetType(), name).Invoke(pose, args);
        var actor = AimActors[combat.Player.Creature];
        // Earlier scenarios attack in both directions. Reset the committed facing,
        // not just the visual transform, before asserting leftward recoil.
        AccessTools.Method(typeof(ShurikenOrb).Assembly.GetType(
            "NinjaSlayer.Code.ExternalAnimations.NinjaSlayerFacingState"), "SetFacing")
            .Invoke(null, [actor, false]);
        Call("Reset"); Call("SyncNow");
        Vector2 root = actor.Position, baseline = center.GlobalPosition;
        Call("BeginBackflip"); pose._Process(.12);
        object first = LatestPresentation(pose, "Backflip")!;
        Transform2D before = pose.Transform;
        Call("BeginBackflip");
        object second = LatestPresentation(pose, "Backflip")!;
        Require(!ReferenceEquals(first, second) && pose.Transform.IsEqualApprox(before), "Repeated draw lost a flip or snapped its first frame.");
        pose._Process(.14);
        Require(!(bool)AccessTools.Field(first.GetType(), "Active").GetValue(first)!
            && (bool)AccessTools.Field(second.GetType(), "Active").GetValue(second)!, "Finishing an old flip removed the next flip.");
        pose._Process(.12);
        Require(center.GlobalPosition.DistanceTo(baseline) < .1f, "Repeated flips did not settle on the authored core.");
        Call("BeginShurikenThrow", combat.Enemy); pose._Process(.12);
        object throw1 = LatestPresentation(pose, "Throw")!;
        Call("BeginShurikenThrow", combat.Enemy);
        Require(!ReferenceEquals(throw1, LatestPresentation(pose, "Throw")), "Repeated throws replaced the previous throw.");
        pose._Process(.34);
        Require(center.GlobalPosition.DistanceTo(baseline) < .1f, "Repeated throws retained an offset.");

        Call("BeginDebuffShake");
        object debuff = LatestPresentation(pose, "Debuff")!;
        Call("BeginDebuffShake");
        Require(ReferenceEquals(debuff, LatestPresentation(pose, "Debuff")), "Repeated debuffs restarted the host shake.");
        for (int i=1;i<=60;i++)
        {
            pose._Process(1d/60d);
            float p=Math.Min(1f,i/60f), phase=Mathf.Tau*(1f-Mathf.Pow(1f-p,3f));
            Vector2 nativeOffset=actor.GetGlobalTransformWithCanvas().BasisXform(Vector2.Right*10f*Mathf.Sin(phase*4f)*Mathf.Sin(phase*.5f));
            Require(center.GlobalPosition.DistanceTo(baseline+nativeOffset)<.01f, "Debuff shake differs from NCreature.AnimShake's native curve.");
            Require(actor.Position.IsEqualApprox(root), "Debuff shake moved combat UI.");
        }
        Call("Reset"); Call("SyncNow");
        FastModeType originalSpeed = SaveManager.Instance.PrefsSave.FastMode;
        foreach (FastModeType mode in new[] { FastModeType.Normal, FastModeType.Fast })
        {
            SaveManager.Instance.PrefsSave.FastMode = mode;
            Task hurtTask = StaggerAnimation.Play(combat.Player.Creature);
            object hurt = LatestPresentation(pose, "Hurt")!;
            Call("SyncNow");
            Require(center.GlobalPosition.X < baseline.X - 20f && actor.Position.IsEqualApprox(root), "Hurt lacks recoil or moves the UI root.");
            Action resume = (Action)AccessTools.Method(pose.GetType(), "PauseHurt").Invoke(pose, null)!;
            pose._Process(.1);
            Require((float)AccessTools.Field(hurt.GetType(), "Elapsed").GetValue(hurt)! == 0f, "Paused hurt advanced.");
            resume(); pose._Process(.15);
            Require(!hurtTask.IsCompleted, "Hurt finished at 0.15s instead of the reference 0.3s.");
            Call("BeginBackflip");
            pose._Process(.151);
            Require(LatestPresentation(pose, "Hurt") == null && LatestPresentation(pose, "Backflip") != null, "Hurt recovery swallowed the overlapping flip.");
            Call("Reset"); pose._Process(1);
            Require(actor.Position.IsEqualApprox(root) && center.GlobalPosition.DistanceTo(baseline) < .1f, "Motion cleanup left a displaced actor.");
            Task blocked = Task.CompletedTask;
            NinjaSlayerAnimationPatch.Prefix(combat.Player.Creature, "BlockedHit", 0f, ref blocked, out _);
            pose._Process(.11);
            Require(LatestPresentation(pose, "Brace") != null, "Full block finished at 0.1s instead of the original 0.2s.");
            pose._Process(.091);
            Require(LatestPresentation(pose, "Brace") == null && pose.Transform.IsEqualApprox(Transform2D.Identity),
                "Full block did not finish and restore at 0.2s.");

            Type dodgeType = typeof(StaggerAnimation).Assembly.GetType("NinjaSlayer.Code.ExternalAnimations.CombatDodgeAnimation", true)!;
            Task dodge = (Task)AccessTools.Method(dodgeType, "PlayImmediate").Invoke(null, [combat.Player.Creature])!;
            var states = (System.Collections.IDictionary)AccessTools.Field(dodgeType, "ActiveDodges").GetValue(null)!;
            object state = states[combat.Player.Creature]!;
            Tween ActiveTween() => (Tween)AccessTools.Property(state.GetType(), "ActiveTween").GetValue(state)!;
            Vector2 Offset() => (Vector2)AccessTools.Field(LatestPresentation(pose, "Offset")!.GetType(), "Offset")
                .GetValue(LatestPresentation(pose, "Offset"))!;
            Tween outbound = ActiveTween();
            outbound.Pause(); outbound.CustomStep(.04);
            Require(Offset().Length() < 119.9f, "Dodge reached its 120px peak in half of the original 0.08s.");
            outbound.CustomStep(.041);
            for (int frame = 0; ReferenceEquals(outbound, ActiveTween()) && frame < 90; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Tween returning = ActiveTween();
            Require(!ReferenceEquals(returning, outbound), "Dodge did not begin its return.");
            returning.Pause(); returning.CustomStep(.07);
            Require(Offset().Length() > 1f, "Dodge returned in 0.07s instead of the original 0.14s.");
            returning.CustomStep(.071);
            await dodge;
            Require(actor.Position.IsEqualApprox(root) && center.GlobalPosition.DistanceTo(baseline) < .1f,
                "Dodge failed to restore the visual pose without moving the UI.");
        }
        SaveManager.Instance.PrefsSave.FastMode = originalSpeed;
        GD.Print("PASS independent draws/throws, native debuff curve, hurt recoil/pause, UI stability and lease cleanup.");
    }

    private void VerifyArchitectNativeDeath()
    {
        string extension=System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../../addons/spine/spine_godot_extension.gdextension"));
        GDExtensionManager.LoadExtension(extension);
        Resource original=GD.Load<Resource>("res://animations/monsters/architect/architect_skel_data.tres");
        Resource augmented=original.Duplicate();
        augmented.Set("skeleton_file_res",GD.Load<Resource>("res://NinjaSlayer/animations/architect/architect.spskel"));
        Node2D Make(Resource data)
        {
            var node=(Node2D)ClassDB.Instantiate("SpineSprite").AsGodotObject();
            node.Call("set_skeleton_data_res",data);node.Call("set_update_mode",2);node.Scale=Vector2.One*.35f;
            AddChild(node); node.Call("update_skeleton",0f);return node;
        }
        Node2D native=Make(original), candidate=Make(augmented);
        try
        {
            using Variant ns=native.Call("get_animation_state"), cs=candidate.Call("get_animation_state");
            foreach(string animation in new[]{"idle_loop","attack","hurt","_tracks/head_normal","_tracks/head_reading","_tracks/head_reading2","_tracks/head_stop_reading","_tracks/head_stop_reading2"})
            {
                using Variant nt=ns.AsGodotObject().Call("set_animation",animation,false,0),ct=cs.AsGodotObject().Call("set_animation",animation,false,0);
                for(int f=0;f<20;f++)
                {
                    native.Call("update_skeleton",1f/60f);candidate.Call("update_skeleton",1f/60f);
                    foreach(string bone in new[]{"cog","body_lower","body_upper","head","hand_f","foot1_b","cape f4"})
                        Require(native.Call("get_global_bone_transform",bone).AsTransform2D().IsEqualApprox(candidate.Call("get_global_bone_transform",bone).AsTransform2D()),$"Private Architect resource changed {animation}/{bone}.");
                }
            }
            using Variant hurt=cs.AsGodotObject().Call("set_animation","hurt",false,0);
            hurt.AsGodotObject().Call("set_track_time",.1f);hurt.AsGodotObject().Call("set_time_scale",0f);
            candidate.Call("update_skeleton",0f);
            Vector2 head=candidate.Call("get_global_bone_transform","head").AsTransform2D().Origin;
            Transform2D shadow=candidate.Call("get_global_bone_transform","shadow").AsTransform2D();
            using Variant death=cs.AsGodotObject().Call("set_animation","ninjaslayer_soft_death",false,0);
            death.AsGodotObject().Call("set_mix_duration",.05f);
            for(int f=0;f<54;f++) candidate.Call("update_skeleton",1f/60f);
            Require(Math.Abs(death.AsGodotObject().Call("get_track_time").AsSingle()-.9f)<.001f,"Native Architect death timing drifted.");
            Require(candidate.Call("get_global_bone_transform","head").AsTransform2D().Origin.Y>head.Y+80f,"Architect native death did not sag toward the floor.");
            foreach (string bone in new[] { "head", "body_lower", "foot_f1", "foot1_b" })
            {
                Vector2 settled = candidate.Call("get_global_bone_transform", bone).AsTransform2D().Origin;
                Require(Math.Abs(settled.Y - candidate.GlobalPosition.Y) < 90f,
                    $"Architect {bone} stayed suspended instead of folding near the floor: {settled}.");
            }
            Transform2D finalShadow=candidate.Call("get_global_bone_transform","shadow").AsTransform2D();
            Require(Math.Abs(finalShadow.Origin.Y-shadow.Origin.Y)<.1f,$"Architect death moved its ground baseline: {shadow.Origin} -> {finalShadow.Origin}.");
            GD.Print("PASS native Architect Spine: eight original tracks unchanged, private death track 0.9s, continuous sag and fixed ground shadow.");
        }
        finally {native.Free();candidate.Free();augmented.Dispose();}
    }
}
