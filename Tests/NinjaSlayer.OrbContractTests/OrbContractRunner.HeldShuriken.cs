using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Orbs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Orbs;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private async Task VerifyHeldShurikenInertia(OrbCombat combat, NCreature actor, Node2D anchor)
    {
        await AddStock(combat.Player, 3);
        ShurikenOrb model = combat.Player.PlayerCombatState!.OrbQueue.Orbs.OfType<ShurikenOrb>().Single();
        var orb = new HeldContractOrb();
        AccessTools.Property(typeof(NOrb), "Model").SetValue(orb, model);
        var labels = new Control { Name = "LabelContainer", Position = new(-24f, -24f) };
        orb.AddChild(labels);
        labels.Owner = orb;
        labels.UniqueNameInOwner = true;
        var visual = (ShurikenOrbVisual)model.CreateSprite()!;
        orb.AddChild(visual);
        actor.AddChild(orb);
        visual.SetProcess(false);
        Sprite2D body = visual.GetNode<Sprite2D>("DeformedVisuals/Art/Body");
        Sprite2D near = visual.GetNode<Sprite2D>("DeformedVisuals/Art/RearNear");
        Sprite2D far = visual.GetNode<Sprite2D>("DeformedVisuals/Art/RearFar");
        Sprite2D glow = visual.GetNode<Sprite2D>("DeformedVisuals/Art/EdgeGlow");
        Transform2D baseline = anchor.Transform;
        Vector2 labelPosition = labels.Position, labelScale = labels.Scale;
        FastModeType speed = SaveManager.Instance.PrefsSave.FastMode;
        void Sync() => AccessTools.Method(typeof(ShurikenOrbVisual), "SyncNow").Invoke(visual, null);
        void Release() => AccessTools.Method(typeof(ShurikenOrbVisual), "OnShurikenReleased").Invoke(visual, [1f]);
        float Velocity() => (float)AccessTools.Field(typeof(ShurikenOrbVisual), "_angularSpeed").GetValue(visual)!;
        void Stock(int count) { AccessTools.Property(typeof(ShurikenOrb), "StackCount").SetValue(model, count); Sync(); }
        try
        {
            foreach (int count in new[] { 0, 1, 2, 3, 99 })
            {
                Stock(count);
                Require(visual.Visible == (count > 0) && labels.Visible == (count > 0), "Empty stock retained art or numbers.");
                if (count == 0) continue;
                Require(near.Visible == (count >= 2) && far.Visible == (count >= 3), "Stock did not show one/two/three layers.");
                Require(near.Scale == body.Scale && far.Scale == body.Scale && near.Position == body.Position
                    && far.Position == body.Position && near.Modulate.A == 1f && far.Modulate.A == 1f,
                    "Stacked blades changed size, shifted the hand center or became transparent ghosts.");
            }
            Stock(3);
            foreach (FastModeType mode in new[] { FastModeType.Normal, FastModeType.Fast })
            foreach (int fps in new[] { 30, 60, 144 })
            foreach (float facing in new[] { 1f, -1f })
            {
                SaveManager.Instance.PrefsSave.FastMode = mode;
                anchor.Transform = new Transform2D(.3f, new Vector2(facing * 1.3f, .8f), .12f, new(0f, -90f));
                Sync();
                Vector2 hand = orb.Position;
                float initial = body.RotationDegrees;
                float handedness = body.GetGlobalTransformWithCanvas().Determinant() < 0f ? -1f : 1f;
                Release();
                Require(body.RotationDegrees == initial, "Release reset the held stack's current angle.");
                for (int frame = 0; frame < fps / 2; frame++) visual._Process(1d / fps);
                float turn = Mathf.PosMod((body.RotationDegrees - initial) * handedness, 360f);
                Require(turn is > 59f and <= 60.1f && Velocity() == 0f, $"Held impulse was {turn} degrees at {fps}fps/{mode}.");
                Require(orb.Position.DistanceTo(hand) < .01f && labels.Position == labelPosition
                    && labels.Scale == labelScale && labels.Rotation == 0f && labels.ZIndex == 4,
                    "Inertial rotation moved the hand center or transformed its labels.");
                Require(glow.RotationDegrees == body.RotationDegrees, "Contour glow did not rotate with the blade.");
            }
            anchor.Transform = baseline;
            Sync();
            Release();
            visual._Process(.05);
            float angle = body.RotationDegrees, previousVelocity = Velocity();
            Release();
            Require(body.RotationDegrees == angle && Math.Abs(Velocity()) > Math.Abs(previousVelocity),
                "A consecutive release restarted the stack instead of adding inertia.");
            for (int index = 0; index < 10; index++) Release();
            Require(Math.Abs(Velocity()) <= 1800f, "Repeated releases exceeded the held-spin limit.");
            Stock(0);
            Require(Velocity() == 0f && !visual.Visible && !labels.Visible, "Depleted stock retained spinning art.");
            Stock(1);
            AccessTools.Method(typeof(ShurikenOrb), "ActivatePassiveFeedback").Invoke(model, null);
            Require(Velocity() == 0f, "Gaining stock triggered launch inertia.");
            SaveManager.Instance.PrefsSave.FastMode = FastModeType.Instant;
            int tweens = GetTree().GetProcessedTweens().Count;
            Release();
            Require(Velocity() == 0f && GetTree().GetProcessedTweens().Count == tweens,
                "Instant release created delayed spin or a Tween.");
            GD.Print("PASS held shuriken: opaque equal-size stack, actual impulse direction, 30/60/144fps, mirrored/skewed hand, upright labels, chained spin, depletion and Instant.");
        }
        finally
        {
            anchor.Transform = baseline;
            SaveManager.Instance.PrefsSave.FastMode = speed;
            Stock(0);
            orb.Free();
            AccessTools.Method(typeof(ShurikenOrb), "RemoveDepletedOrb").Invoke(model, null);
        }
    }
}

public partial class HeldContractOrb : NOrb
{
    public override void _EnterTree() { }
    public override void _Ready() { }
    public override void _ExitTree() { }
}
