using System.Collections;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Orbs;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private async Task VerifyStandardComboRecovery(OrbCombat combat, Node2D pose)
    {
        var product = typeof(ShurikenOrb).Assembly;
        Type coordinator = product.GetType("NinjaSlayer.Code.ExternalAnimations.NinjaSlayerRapidAnimationCoordinator", true)!;
        Type execution = product.GetType("NinjaSlayer.Code.Combat.NinjaSlayerAttackExecution", true)!;
        Type pacing = product.GetType("NinjaSlayer.Code.Combat.CombatPresentationPacingScope", true)!;
        Type scope = product.GetType("NinjaSlayer.Code.Lifecycle.CardPlayResolutionScope", true)!;
        object? Run(string name, params object?[] args) => AccessTools.Method(coordinator, name).Invoke(null, args);
        object State() => ((IDictionary)AccessTools.Field(coordinator, "States").GetValue(null)!)[combat.Player.Creature]!;
        IList Motions() => (IList)AccessTools.Property(State().GetType(), "Motions").GetValue(State())!;
        Vector2 Travel() => (Vector2)AccessTools.Property(pose.GetType(), "Travel").GetValue(pose)!;
        Task Attack(float distance, float seconds, float recoverySeconds, bool held = false) => (Task)Run("PlayAttackToPeak", combat.Player.Creature,
            distance, seconds, (Func<float, float>)(p => p), false, recoverySeconds, true, held, false)!;
        Task Recover() => (Task)AccessTools.Method(pacing, "WaitForDamageRecovery").Invoke(null,
            [0.1f, 0.2f, false, CancellationToken.None])!;

        FastModeType originalSpeed = SaveManager.Instance.PrefsSave.FastMode;
        SaveManager.Instance.PrefsSave.FastMode = FastModeType.Normal;
        var existingTweens = GetTree().GetProcessedTweens().Select(t => t.GetInstanceId()).ToHashSet();
        Task customGate = NinjaSlayer.Code.ExternalAnimations.FastAttackAnimation.Play(combat.Player.Creature, .4f);
        Tween customTween = GetTree().GetProcessedTweens().Single(t => !existingTweens.Contains(t.GetInstanceId()));
        customTween.Pause();
        customTween.CustomStep(.15f);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(!customGate.IsCompleted, "Attack replaced the caller's 0.4s gate with the default 0.15s.");
        customTween.CustomStep(.249f);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(!customGate.IsCompleted, "Attack released gameplay before the caller's 0.4s gate.");
        customTween.CustomStep(.002f);
        await customGate;
        await ToSignal(GetTree().CreateTimer(.11), SceneTreeTimer.SignalName.Timeout);
        Run("CancelAndRestore", combat.Player.Creature);
        var card = combat.State.CreateCard<RoundhouseKickRedesignV1>(combat.Player);
        var play = new CardPlay { Card = card,
#if !NINJASLAYER_CHANNEL_STABLE
            Player = combat.Player,
#endif
            Target = combat.Enemy, ResultPile = PileType.Discard,
            Resources = new ResourceInfo { EnergySpent = 0, EnergyValue = 0, StarsSpent = 0, StarValue = 0 },
            IsAutoPlay = false, PlayIndex = 0, PlayCount = 1 };
        object resolution = AccessTools.Method(scope, "BeginCard").Invoke(null, [card])!;
        AccessTools.Method(scope, "BeginPlay").Invoke(null, [play]);
        var command = DamageCmd.Attack(1)
#if NINJASLAYER_CHANNEL_STABLE
            .FromCard(card)
#else
            .FromCard(card, play)
#endif
            .Targeting(combat.Enemy);
        object commandLease = AccessTools.Method(execution, "Enter").Invoke(null, [command, combat.Enemy])!;
        object sequence = AccessTools.Method(execution, "EnterSequence").Invoke(null, [2])!;
        try
        {
            var companionNode = combat.Enemy.GetCreatureNode()!;
            Vector2 companionBaseline = companionNode.Visuals.Position;
            Vector2 companionRoot = companionNode.Position;
            Transform2D companionTransform = companionNode.Visuals.Transform;
            foreach (var (mode, gate) in new[] { (FastModeType.Normal, .5f), (FastModeType.Fast, .25f), (FastModeType.Instant, 0f) })
            {
                SaveManager.Instance.PrefsSave.FastMode = mode;
                var before = GetTree().GetProcessedTweens().Select(t => t.GetInstanceId()).ToHashSet();
                Task slow = (Task)AccessTools.Method(product.GetType("NinjaSlayer.Code.ExternalAnimations.SlowAttackAnimation", true)!,
                    "PlayIai").Invoke(null, [combat.Enemy])!;
                Tween[] created = GetTree().GetProcessedTweens().Where(t => !before.Contains(t.GetInstanceId())).ToArray();
                if (gate > 0f)
                {
                    Tween motion = created.Single();
                    motion.Pause();
                    motion.CustomStep(gate / 2f);
                    Require(Math.Abs(companionNode.Visuals.Position.X - companionBaseline.X + 120f / 1024f) < .01f,
                        "Iai did not spread its pow10 outbound across the full slash gate.");
                    motion.CustomStep(gate / 2f - .001f);
                    Require(!slow.IsCompleted, "Companion Slow released damage early inside a kick card.");
                    motion.CustomStep(.0011f);
                    Require(Math.Abs(companionNode.Visuals.Position.X - companionBaseline.X + 120f) < .1f,
                        "Iai inherited the player's kick preparation instead of reaching 120px at its slash gate.");
                    await slow;
                    motion.CustomStep(.1249f);
                    Require(Math.Abs(companionNode.Visuals.Position.X - companionBaseline.X + 60f) < .1f,
                        "Iai did not use a 0.25s return independent of Normal/Fast.");
                    motion.CustomStep(.126f);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                else Require(created.Length == 0, "Instant companion Slow created a zero-duration Tween.");
                await slow;
                Require(companionNode.Position.IsEqualApprox(companionRoot)
                    && companionNode.Visuals.Position.IsEqualApprox(companionBaseline)
                    && companionNode.Visuals.Transform.IsEqualApprox(companionTransform),
                    "Companion Slow changed its UI root, inherited a kick, or failed to return.");
            }
            GD.Print("PASS Iai inside an active kick: 120px pow10, 0.50/0.25s gates and outbound, 0.25s return, Instant without Tween.");
            foreach (var (speed, scale) in new[] { (FastModeType.Normal, 1f), (FastModeType.Fast, 0.5f), (FastModeType.Instant, 0f) })
            foreach (float distance in new[] { 90f, 120f })
            {
                SaveManager.Instance.PrefsSave.FastMode = speed;
                Run("CancelAndRestore", combat.Player.Creature);
                float gate = 0.25f * scale;
                for (int hit = 0; hit < 2; hit++)
                {
                    AccessTools.Method(sequence.GetType(), "SetHit").Invoke(sequence, [hit]);
                    Task attack = Attack(distance, gate, .2f * scale);
                    object motion = Motions()[^1]!;
                    if (scale > 0f)
                    {
                        var outbound = (Tween)AccessTools.Field(motion.GetType(), "Tween").GetValue(motion)!;
                        outbound.Pause();
                        outbound.CustomStep(.0125f);
                        if (hit == 0) Require(Travel().Length() < .1f, "Kick moved before its preparation turn finished.");
                        outbound.CustomStep(gate - .0135f);
                        Require(!attack.IsCompleted, "Kick released gameplay before its full gate.");
                        outbound.CustomStep(.002f);
                    }
                    await attack;
                    Require(Math.Abs(Travel().Length() - distance) < .1f, "Combo peak accumulated displacement.");
                    if (hit != 0) continue;
                    Task recovery = Recover();
                    if (scale > 0f)
                    {
                        Require((bool)AccessTools.Field(motion.GetType(), "Returning").GetValue(motion)!,
                            "Standard damage recovery held the first kick at its peak until the second kick.");
                        var returning = (Tween)AccessTools.Field(motion.GetType(), "Tween").GetValue(motion)!;
                        returning.Pause();
                        returning.CustomStep(.05f);
                        Require(Math.Abs(Travel().Length() - distance / 2f) < .1f, "Combo recovery does not use the standard recovery duration.");
                        returning.CustomStep(.051f);
                    }
                    await recovery;
                    Require(Travel().Length() < .1f, "Next combo hit would start before the preceding lunge recovered.");
                    Require((float)AccessTools.Field(pose.GetType(), "_kick").GetValue(pose)! == 1f,
                        "Per-hit lunge recovery reset the shared kick stance.");
                }
                Run("CardGameplaySettled", combat.Player.Creature);
                if (scale > 0f)
                {
                    var finalReturn = (Tween)AccessTools.Property(State().GetType(), "ActiveTween").GetValue(State())!;
                    finalReturn.Pause();
                    finalReturn.CustomStep(.2f * scale + .001f);
                }
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Require(pose.Transform.IsEqualApprox(Transform2D.Identity), "Combo final recovery retained its kick pose.");
            }
            SaveManager.Instance.PrefsSave.FastMode = FastModeType.Normal;
            AccessTools.Method(sequence.GetType(), "SetHit").Invoke(sequence, [0]);
            Task tornado = Attack(120f, .15f, .2f, held: true);
            object heldMotion = Motions()[^1]!;
            var heldTween = (Tween)AccessTools.Field(heldMotion.GetType(), "Tween").GetValue(heldMotion)!;
            heldTween.Pause();
            heldTween.CustomStep(.151f);
            await tornado;
            await Recover();
            Require(!(bool)AccessTools.Field(heldMotion.GetType(), "Returning").GetValue(heldMotion)!
                && Math.Abs(Travel().Length() - 120f) < .1f, "Tornado's shared lunge returned between hits.");
            GD.Print("PASS same-card Attack/Slow kick recovery: Normal/Fast/Instant, full gates, per-hit return, shared kick stance and held Tornado exclusion.");
        }
        finally
        {
            Run("CancelAndRestore", combat.Player.Creature);
            SaveManager.Instance.PrefsSave.FastMode = originalSpeed;
            ((IDisposable)sequence).Dispose();
            AccessTools.Method(commandLease.GetType(), "RestoreCaller").Invoke(commandLease, null);
            AccessTools.Method(scope, "CompleteCard").Invoke(null, [resolution]);
        }
    }
}
