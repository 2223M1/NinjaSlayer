using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Content;
using NinjaSlayer.Orbs;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private void VerifyFinisherApproach(NCreature actor, NCreature focus)
    {
        var command = DamageCmd.Attack(1).WithHeavyBluntHitFx();
        Require(command.HitVfx == VfxCmd.heavyBluntPath
            && !(bool)AccessTools.Field(command.GetType(), "_spawnVfxOnCreatureCenter").GetValue(command)!,
            "Heavy attacks did not use the complete native ground-spawned effect.");
        Type type = typeof(ShurikenOrb).Assembly.GetType("NinjaSlayer.Code.ExternalAnimations.FinisherApproach", true)!;
        Node originalParent = actor.Visuals.GetParent();
        Vector2 actorRoot = actor.Position, focusRoot = focus.Position, baseline = actor.Visuals.Position;
        var bounds = new Control { Position = new(-50f, -150f), Size = new(100f, 150f) };
        focus.Visuals.AddChild(bounds);
        AccessTools.Property(typeof(NCreatureVisuals), "Bounds").SetValue(focus.Visuals, bounds);
        int treeExits = 0;
        void Exited() => treeExits++;
        actor.Visuals.TreeExiting += Exited;
        try
        {
            foreach (float facing in new[] { -1f, 1f })
            {
                focus.Position = actorRoot + new Vector2(facing * 600f, 0f);
                var lease = (IDisposable)AccessTools.Method(type, "Create").Invoke(null, [actor, focus, Vector2.One])!;
                try
                {
                    Require(actor.Visuals.Position.IsEqualApprox(baseline), "Finisher approach teleported on its first frame.");
                    Vector2 endpoint = (Vector2)AccessTools.Field(type, "_destination").GetValue(lease)!;
                    for (int frame = 0; frame <= 20; frame++)
                    {
                        float p = frame / 20f;
                        AccessTools.Method(type, "ApplyProgress").Invoke(lease, [p]);
                        Vector2 expected = baseline + endpoint * (1f - MathF.Pow(1f - p, 3f));
                        Require(actor.Visuals.Position.DistanceTo(expected) < .01f, "Approach diverged from the continuous cubic path.");
                        Require(actor.Position.IsEqualApprox(actorRoot), "Finisher approach moved the health/status root.");
                        Require(actor.Visuals.GetParent() == originalParent && treeExits == 0,
                            "Approach re-entered the character rig and reset its animation lifecycle.");
                    }
                    Vector2 shake = baseline + new Vector2(4f, 0f);
                    AccessTools.Method(type, "SetAnimationPosition").Invoke(null, [actor.Entity, actor.Visuals, shake]);
                    Require(actor.Visuals.Position.DistanceTo(shake + endpoint) < .01f, "Hurt recovery erased the approach.");
                    AccessTools.Method(type, "SetAnimationPosition").Invoke(null, [actor.Entity, actor.Visuals, baseline]);
                    AccessTools.Method(type, "BeginReturn").Invoke(lease, null);
                    object?[] nextAction = [actor.Entity, .15f, null];
                    Require(!(bool)AccessTools.Method(type, "TryPlayToPeak").Invoke(null, nextAction)!,
                        "A prediction-mismatch return swallowed the next attack animation.");
                    AccessTools.Method(type, "ApplyReturn").Invoke(lease, [.5f]);
                    Vector2 returnFrame = actor.Visuals.Position;
                    AccessTools.Method(type, "ReachImpact").Invoke(null, [actor.Entity]);
                    Require(actor.Visuals.Position.IsEqualApprox(returnFrame),
                        "A later hit pulled a prediction-mismatch return back to the impact point.");
                    AccessTools.Method(type, "ApplyReturn").Invoke(lease, [1f]);
                    Require(actor.Visuals.Position.IsEqualApprox(baseline), "Approach return failed to restore its own baseline.");
                }
                finally { lease.Dispose(); }
            }
            foreach (float seconds in new[] { .25f, 0f })
            {
                using var lease = (IDisposable)AccessTools.Method(type, "Create").Invoke(null, [actor, focus, Vector2.One])!;
                AccessTools.Property(type, "ReturnDuration").SetValue(lease, seconds);
                AccessTools.Method(type, "ApplyProgress").Invoke(lease, [1f]);
                Vector2 peak = actor.Visuals.Position;
                var before = GetTree().GetProcessedTweens().Select(t => t.GetInstanceId()).ToHashSet();
                AccessTools.Method(type, "ReleasePrediction").Invoke(lease, null);
                Tween[] created = GetTree().GetProcessedTweens().Where(t => !before.Contains(t.GetInstanceId())).ToArray();
                if (seconds > 0f)
                {
                    Tween returning = created.Single();
                    returning.Pause();
                    returning.CustomStep(.125f);
                    Require(actor.Visuals.Position.DistanceTo(peak.Lerp(baseline, .5f)) < .01f,
                        "Iai prediction mismatch did not preserve the selected 0.25s return.");
                    returning.CustomStep(.126f);
                }
                else Require(created.Length == 0, "Instant Iai prediction return created a zero-duration Tween.");
                Require(actor.Visuals.Position.IsEqualApprox(baseline) && actor.Position.IsEqualApprox(actorRoot),
                    "Iai prediction return failed to restore the visual baseline with a stable UI root.");
            }
        }
        finally
        {
            actor.Visuals.TreeExiting -= Exited;
            focus.Position = focusRoot;
            actor.Visuals.Position = baseline;
        }
        GD.Print("PASS native heavy base effect and finisher approach: both directions, continuous start/impact/return, stable UI, no rig reparenting.");
    }
}
