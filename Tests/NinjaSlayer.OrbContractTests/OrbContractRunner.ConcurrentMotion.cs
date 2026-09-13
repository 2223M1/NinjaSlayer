using System.Collections;
using Godot;
using HarmonyLib;
using NinjaSlayer.Orbs;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private async Task VerifyConcurrentMotion(OrbCombat combat, Node2D pose, Node2D anchor, Marker2D center, AimContractCreature target)
    {
        var assembly = typeof(ShurikenOrb).Assembly;
        Type coordinator = assembly.GetType("NinjaSlayer.Code.ExternalAnimations.NinjaSlayerRapidAnimationCoordinator", true)!;
        Type rapid = assembly.GetType("NinjaSlayer.Code.Lifecycle.RapidCardPresentationContext", true)!;
        var actor = AimActors[combat.Player.Creature];
        void Pose(string name, params object?[] args) => AccessTools.Method(pose.GetType(), name).Invoke(pose, args);
        object? Run(string name, params object?[] args) => AccessTools.Method(coordinator, name).Invoke(null, args);
        Task Attack(float distance, float seconds) => (Task)Run("PlayAttackToPeak", combat.Player.Creature,
            distance, seconds, (Func<float, float>)(p => p), false, 0.2f, false, false)!;
        object State() => ((IDictionary)AccessTools.Field(coordinator, "States").GetValue(null)!)[combat.Player.Creature]!;
        Tween MotionTween(object state, int index)
        {
            object motion = ((IList)AccessTools.Property(state.GetType(), "Motions").GetValue(state)!)[index]!;
            return (Tween)AccessTools.Field(motion.GetType(), "Tween").GetValue(motion)!;
        }
        var targetCenter = target.Visuals.VfxSpawnPosition;
        Vector2 targetCenterBaseline = targetCenter.Position;
        Vector2 targetBaseline = target.Position;
        Vector2 actorBaseline = actor.Position;
        Transform2D anchorBaseline = anchor.Transform;
        object lease = AccessTools.Method(rapid, "Begin").Invoke(null, [combat.Card()])!;
        pose.SetProcess(false);
        try
        {
            foreach (float facing in new[] { 1f, -1f })
            {
                Run("CancelAndRestore", combat.Player.Creature);
                actor.Position = actorBaseline;
                anchor.Transform = Transform2D.Identity;
                anchor.Scale = new(facing, 1f);
                Pose("SyncNow");
                target.Position = new(facing * 150f, 0f);
                targetCenter.Position = new(0f, center.GlobalPosition.Y);
                Vector2 attackCore = center.GlobalPosition;
                Task first = Attack(120f, 0.2f);
                object state = State();
                Tween firstTween = MotionTween(state, 0);
                firstTween.Pause();
                firstTween.CustomStep(0.05);
                float firstOffset = center.GlobalPosition.X - attackCore.X;
                Require(Math.Abs(firstOffset - facing * 30f) < 0.01f, "First attack did not advance independently.");
                Task second = Attack(90f, 0.15f);
                Tween secondTween = MotionTween(state, 1);
                secondTween.Pause();
                Require(firstTween.IsValid() && !first.IsCompleted,
                    "Second attack cancelled the first attack's active tween.");
                Require(Math.Abs(center.GlobalPosition.X - attackCore.X - firstOffset) < 0.01f,
                    "Second attack snapped the first attack back to baseline.");
                firstTween.CustomStep(0.151);
                secondTween.CustomStep(0.151);
                await Task.WhenAll(first, second);
                Require(Math.Abs(center.GlobalPosition.X - attackCore.X - facing * 210f) < 0.1f,
                    $"Overlapping attacks failed to sum full displacement: {center.GlobalPosition.X - attackCore.X}.");
                Require(actor.Position.IsEqualApprox(actorBaseline), "Ordinary attacks moved the combat UI root.");
                Task third = Attack(120f, 0.2f);
                Tween firstRecovery = MotionTween(state, 0), secondRecovery = MotionTween(state, 1);
                firstRecovery.Pause();
                secondRecovery.Pause();
                Tween thirdTween = MotionTween(state, 2);
                thirdTween.Pause();
                Run("CardGameplaySettled", combat.Player.Creature);
                Require(AccessTools.Property(state.GetType(), "ActiveTween").GetValue(state) == null,
                    "Return started while an attack was still advancing.");
                firstRecovery.CustomStep(0.201);
                secondRecovery.CustomStep(0.201);
                thirdTween.CustomStep(0.201);
                await third;
                Vector2 thirdTravel = (Vector2)AccessTools.Property(pose.GetType(), "Travel").GetValue(pose)!;
                Require(Math.Abs(thirdTravel.Length() - 120f) < 0.1f && thirdTravel.X * facing > 0f,
                    $"Completed attacks retained their displacement over the next attack: {thirdTravel}.");
                Require(((Vector2)Run("GetBaseline", combat.Player.Creature, actor)!).DistanceTo(actorBaseline) < 0.001f,
                    "Repeated attacks replaced the original return baseline.");
                Tween returning = (Tween)AccessTools.Property(state.GetType(), "ActiveTween").GetValue(state)!;
                returning.Pause();
                returning.CustomStep(0.08);
                Vector2 midReturn = center.GlobalPosition;
                Task fourth = Attack(90f, 0.15f);
                IList motions = (IList)AccessTools.Property(state.GetType(), "Motions").GetValue(state)!;
                Tween fourthTween = MotionTween(state, motions.Count - 1);
                fourthTween.Pause();
                Require(center.GlobalPosition.DistanceTo(midReturn) < .1f, "Return-to-attack snapped the current pose.");
                Tween thirdRecovery = MotionTween(state, motions.Count - 2);
                thirdRecovery.Pause();
                thirdRecovery.CustomStep(.201);
                fourthTween.CustomStep(.151);
                await fourth;
                Vector2 fourthTravel = (Vector2)AccessTools.Property(pose.GetType(), "Travel").GetValue(pose)!;
                Require(Math.Abs(fourthTravel.Length() - 90f) < .1f && fourthTravel.X * facing > 0f,
                    "An old return overwrote the new attack or retained an offset.");
                Run("CardGameplaySettled", combat.Player.Creature);
                returning = (Tween)AccessTools.Property(state.GetType(), "ActiveTween").GetValue(state)!;
                returning.Pause();
                returning.CustomStep(.201);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Require(actor.Position.DistanceTo(actorBaseline) < 0.001f && pose.Transform.IsEqualApprox(Transform2D.Identity),
                    "Overlapping attacks failed to restore the original root and pose.");
            }
            await VerifyStandardComboRecovery(combat, pose);
            Run("CancelAndRestore", combat.Player.Creature);
            anchor.Transform = Transform2D.Identity;
            actor.Position = actorBaseline;
            target.Position = new(700f, 0f);
            Pose("SyncNow");
            Vector2 coreBaseline = center.GlobalPosition;
            VerifyIndependentPresentation(combat, pose, center);
            Pose("BeginBackflip");
            Pose("BeginShurikenThrow", combat.Enemy);
            Pose("BeginAirMotion", false);
            Task combinedAttack = Attack(90f, 0.15f);
            object combinedState = State();
            Tween combinedTween = MotionTween(combinedState, 0);
            combinedTween.Pause();
            combinedTween.CustomStep(0.075);
            pose._Process(0.10001);
            Require((bool)AccessTools.Property(pose.GetType(), "IsBackflipping").GetValue(pose)!
                && (bool)AccessTools.Property(pose.GetType(), "IsJumping").GetValue(pose)!
                && ThrowReleased(pose)
                && center.GlobalPosition.X > coreBaseline.X,
                "Actual attack movement suppressed flip, jump or throw release.");
            Task combinedSecond = Attack(120f, 0.2f);
            Tween combinedSecondTween = MotionTween(combinedState, 1);
            combinedSecondTween.Pause();
            combinedTween.CustomStep(0.076);
            combinedSecondTween.CustomStep(0.201);
            await Task.WhenAll(combinedAttack, combinedSecond);
            Run("CardGameplaySettled", combat.Player.Creature);
            Tween combinedReturn = (Tween)AccessTools.Property(combinedState.GetType(), "ActiveTween").GetValue(combinedState)!;
            combinedReturn.Pause();
            combinedReturn.CustomStep(0.201);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require((bool)AccessTools.Property(pose.GetType(), "IsBackflipping").GetValue(pose)!
                && (bool)AccessTools.Property(pose.GetType(), "IsJumping").GetValue(pose)!
                && MotionNumber(pose, "Throw", "Duration") > 0f,
                "Completing the actual attack return cleared independent motions.");
            pose._Process(0.626);
            Require(actor.Position.DistanceTo(actorBaseline) < 0.001f
                && center.GlobalPosition.DistanceTo(coreBaseline) < 0.1f,
                "Combined attack, flip, jump and throw retained an offset.");
            Pose("BeginAirMotion", false);
            pose._Process(0.1);
            Pose("BeginAirMotion", false);
            Pose("BeginAirMotion", true);
            Pose("BeginAction", combat.Enemy, false, false);
            pose._Process(0.1);
            Pose("BeginReturn");
            Pose("ApplyReturn", 1f);
            float expectedLift = 150f * 4f * (0.2f / 0.35f) * (1f - 0.2f / 0.35f)
                + 150f * 4f * (0.1f / 0.35f) * (1f - 0.1f / 0.35f)
                + 60f * Mathf.Sin(0.1f / 0.14f * Mathf.Pi);
            Require(Math.Abs(center.GlobalPosition.Y - coreBaseline.Y + expectedLift) < 0.1f,
                "Repeated jumps, hop and attack did not preserve all airborne contributions.");
            pose._Process(0.601);
            Require(center.GlobalPosition.DistanceTo(coreBaseline) < 0.1f && pose.Transform.IsEqualApprox(Transform2D.Identity),
                "Independent air motions failed to return to baseline.");
            Pose("BeginBackflip");
            Pose("BeginAirMotion", false);
            Task cancelledAttack = Attack(120f, 0.2f);
            Tween cancelledTween = MotionTween(State(), 0);
            cancelledTween.Pause();
            cancelledTween.CustomStep(0.05);
            Run("CancelAndRestore", combat.Player.Creature);
            await cancelledAttack;
            pose._Process(1.0);
            Require(actor.Position.DistanceTo(actorBaseline) < 0.001f && pose.Transform.IsEqualApprox(Transform2D.Identity),
                "Lifecycle cleanup left an active attack or airborne contribution.");
            GD.Print("PASS actual concurrent motion: overlapping 120+90+120 attacks, crossing both sides, original baseline, repeated jumps and hop during attack.");
            VerifyArchitectNativeDeath();
        }
        finally
        {
            Run("CancelAndRestore", combat.Player.Creature);
            AccessTools.Method(lease.GetType(), "RestoreCallerContext").Invoke(lease, null);
            anchor.Transform = anchorBaseline;
            actor.Position = actorBaseline;
            target.Position = targetBaseline;
            targetCenter.Position = targetCenterBaseline;
            Pose("SyncNow");
            pose.SetProcess(true);
        }
    }
}
