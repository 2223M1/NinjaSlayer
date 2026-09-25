using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Content;
using NinjaSlayer.Monsters;
using NinjaSlayer.Orbs;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private void VerifyFinisherApproach(NCreature actor, NCreature focus)
    {
        Type timeline = typeof(ShurikenOrb).Assembly.GetType("NinjaSlayer.Code.ExternalAnimations.FinisherTimeline", true)!;
        bool CanSquash(Creature victim) => (bool)AccessTools.Method(timeline, "AllowsDeathSquash").Invoke(null, [victim])!;
        Require(!CanSquash(actor.Entity) && CanSquash(focus.Entity),
            "Finisher squash must exclude Ninja Slayer without disabling native monster squash.");
        foreach (MonsterModel model in new MonsterModel[] { ModelDb.Monster<SawatariMonster>(),
            ModelDb.Monster<DarkNinjaMonster>(), ModelDb.Monster<YamotoKokiMonster>(),
            ModelDb.Monster<YukanoMonster>(), ModelDb.Monster<YamotoKokiOrigamiMissile>() })
            foreach (CombatSide side in new[] { CombatSide.Enemy, CombatSide.Player })
                Require(!CanSquash(new Creature(model.ToMutable(), side, null)),
                    "Mod character received finisher deformation: " + model.GetType().Name);
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
            using (var lease = (IDisposable)AccessTools.Method(type, "Create").Invoke(null, [actor, focus, Vector2.One])!)
            {
                AccessTools.Method(type, "Start").Invoke(lease, [.2f]);
                var outbound = (Tween)AccessTools.Field(type, "_tween").GetValue(lease)!;
                outbound.Pause();
                outbound.CustomStep(.05f);
                Vector2 beforeClaim = actor.Visuals.Position;
                Require(ReferenceEquals(lease, AccessTools.Method(type, "Claim").Invoke(null, [actor.Entity]))
                    && outbound.IsValid(), "Early combo ownership stopped the continuous approach.");
                outbound.CustomStep(.05f);
                Require(actor.Visuals.Position.DistanceTo(beforeClaim) > .01f,
                    "The approach stopped moving after its finisher claimed it.");
                AccessTools.Method(type, "ReachImpact").Invoke(null, [actor.Entity]);
                Vector2 endpoint = (Vector2)AccessTools.Field(type, "_destination").GetValue(lease)!;
                Require(actor.Visuals.Position.DistanceTo(baseline + endpoint) < .01f && !outbound.IsValid(),
                    "The real hit did not finish and release the approach Tween.");
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
            VerifyWeaponContact(actor, focus, type, bounds);
        }
        finally
        {
            actor.Visuals.TreeExiting -= Exited;
            focus.Position = focusRoot;
            actor.Visuals.Position = baseline;
        }
        GD.Print("PASS native heavy base effect and finisher approach: both directions, continuous start/impact/return, stable UI, no rig reparenting.");
    }

    private void VerifyWeaponContact(NCreature actor, NCreature focus, Type approachType, Control targetBounds)
    {
        Type weaponsType = typeof(ShurikenOrb).Assembly.GetType("NinjaSlayer.Code.Nodes.SawatariWeaponVisuals", true)!;
        var weapons = (Node)Activator.CreateInstance(weaponsType)!;
        weapons.Name = "SawatariWeapons";
        using var pixels = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
        pixels.FillRect(new Rect2I(4, 8, 20, 16), Colors.White);
        using var texture = ImageTexture.CreateFromImage(pixels);
        var body = new Sprite2D { Texture = texture, Position = new(15, -100) };
        var hand = new Sprite2D { Texture = texture, Position = new(150, 0) };
        var knife = new Sprite2D { Texture = texture, Position = new(300, 0) };
        var hidden = new Sprite2D { Texture = texture, Position = new(900, 0), Visible = false };
        actor.Visuals.AddChild(body); body.AddChild(hand); body.AddChild(knife); body.AddChild(hidden);
        AccessTools.Field(weaponsType, "_body").SetValue(weapons, body);
        actor.Visuals.AddChild(weapons);
        Rect2 Bounds() => (Rect2)AccessTools.Method(weaponsType, "GetCombatBounds").Invoke(weapons,
            [actor.GetParent<CanvasItem>().GetGlobalTransformWithCanvas().AffineInverse()])!;
        try
        {
            foreach (float direction in new[] { 1f, -1f })
            {
                focus.Position = actor.Position + Vector2.Right * direction * 600f;
                body.Scale = new(direction, 1f);
                knife.Position = new(300, 0);
                using var approach = (IDisposable)AccessTools.Method(approachType, "Create").Invoke(null,
                    [actor, focus, Vector2.One])!;
                Vector2 origin = actor.Visuals.Position;
                AccessTools.Method(approachType, "ApplyProgress").Invoke(approach, [0f]);
                Require(actor.Visuals.Position.IsEqualApprox(origin), "Weapon reach adjustment teleported at startup.");
                AccessTools.Method(approachType, "ApplyProgress").Invoke(approach, [.5f]);
                knife.Position += Vector2.Right * 40f;
                AccessTools.Method(approachType, "ReachImpact").Invoke(null, [actor.Entity]);
                Rect2 actual = Bounds();
                Rect2 target = actor.GetParent<CanvasItem>().GetGlobalTransformWithCanvas().AffineInverse()
                    * targetBounds.GetGlobalTransformWithCanvas() * new Rect2(Vector2.Zero, targetBounds.Size);
                float front = direction > 0 ? actual.End.X : actual.Position.X;
                float contact = direction > 0 ? target.Position.X : target.End.X;
                Require(Math.Abs(front - contact) < .01f,
                    $"Finisher contact omitted the held weapon or double-counted the approach offset: front={front}, contact={contact}, direction={direction}.");
                Require(actual.Size.X < 400f, "Hidden weapons affected finisher reach.");
                Vector2 endpoint = actor.Visuals.Position;
                body.Position -= Vector2.Right * direction * 30f;
                AccessTools.Method(approachType, "ReachImpact").Invoke(null, [actor.Entity]);
                Require(actor.Visuals.Position.IsEqualApprox(endpoint), "Later hits cancelled the normal combo retreat.");
                body.Position += Vector2.Right * direction * 30f;
                knife.Reparent(actor.GetParent());
                Require(Bounds().Size.X < 200f, "A released weapon remained part of Sawatari's body bounds.");
                knife.Reparent(body, keepGlobalTransform: false);
            }
        }
        finally { actor.Visuals.RemoveChild(weapons); weapons.QueueFree(); body.QueueFree(); }
        GD.Print("PASS mod victims remain rigid; complete held-weapon bounds, mirrored contact and combo retreat.");
    }
}
