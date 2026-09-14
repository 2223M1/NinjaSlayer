using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Unlocks;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Orbs;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static readonly (string Name, float[] Values)[] ChargePreviews =
    [
        ("light", [32f, .25f, 1.04f, .94f, .8f, .004f, 8f]),
        ("medium", [40f, .30f, 1.08f, .90f, 1.3f, .008f, 10f]),
        ("strong", [48f, .35f, 1.12f, .86f, 1.8f, .012f, 12f]),
        ("light-medium", [32f, .25f, 1.04f, .94f, 1.3f, .008f, 10f])
    ];

    private async Task VerifyDragControls(OrbCombat combat, NCreature actor, NCreature target,
        Marker2D targetCenter, NCreatureVisuals rig, int form)
    {
        var assembly = typeof(ShurikenOrb).Assembly;
        Node2D pose = rig.GetNode<Node2D>("%AimPose");
        Node2D anchor = rig.GetNode<Node2D>("AirborneAnchor");
        Sprite2D source = rig.GetNode<Sprite2D>("%Visuals");
        Sprite2D overlay = rig.GetNode<Sprite2D>("AirborneAnchor/AimPose/NarakuVisualOverlay");
        Node blur = rig.GetNode("SpinMotionBlur");
        Type type = pose.GetType();
        void Call(string name, params object?[] args) => AccessTools.Method(type, name).Invoke(pose, args);
        float Angle() => (float)AccessTools.Field(type, "_displayAngle").GetValue(pose)!;
        void Face(bool left) => AccessTools.Method(assembly.GetType("NinjaSlayer.Code.ExternalAnimations.NinjaSlayerFacingState"),
            "SetFacing").Invoke(null, [actor, left]);
        void Tick(float seconds)
        {
            blur._Process(seconds);
            pose._Process(seconds);
            Call("SyncNow");
        }
        var drag = new Node();
        actor.GetParent().AddChild(drag);
        var card = combat.State.CreateCard<SatsubatsuRedesignV1>(combat.Player);
        var tornado = combat.State.CreateCard<TornadoFistRedesignV1>(combat.Player);
        MegaCrit.Sts2.Core.Entities.Cards.PileType.Hand.GetPile(combat.Player).AddInternal(tornado, -1, silent: true);
        var profile = AccessTools.Property(type, "ChargeProfile");
        object savedProfile = profile.GetValue(pose)!;
        Vector2 actorBefore = actor.Position, targetBefore = target.Position;
        Transform2D anchorBefore = anchor.Transform;
        Vector2 targetCenterBefore = targetCenter.Position;
        bool processing = pose.IsProcessing();
        pose.SetProcess(false);
        Creature second = combat.AddEnemy();
        var secondNode = new AimContractCreature { Position = new(700f, 0f) };
        var secondRig = new AimContractVisuals();
        var secondCenter = new Marker2D { Position = new(0f, -20f) };
        secondRig.AddChild(secondCenter);
        AccessTools.Property(typeof(NCreatureVisuals), "VfxSpawnPosition").SetValue(secondRig, secondCenter);
        AccessTools.Property(typeof(NCreature), "Entity").SetValue(secondNode, second);
        AccessTools.Property(typeof(NCreature), "Visuals").SetValue(secondNode, secondRig);
        secondNode.AddChild(secondRig);
        actor.GetParent().AddChild(secondNode);
        AimActors.Add(second, secondNode);
        Player ally = Player.CreateForNewRun<Ironclad>(UnlockState.all, 2);
        ally.InitializeSeed("drag-ally");
        combat.State.AddPlayer(ally);
        ally.ResetCombatState();
        var allyNode = new AimContractCreature { Position = new(500f, 0f) };
        var allyRig = new AimContractVisuals();
        var allyCenter = new Marker2D { Position = new(0f, -400f) };
        allyRig.AddChild(allyCenter);
        AccessTools.Property(typeof(NCreatureVisuals), "VfxSpawnPosition").SetValue(allyRig, allyCenter);
        AccessTools.Property(typeof(NCreature), "Entity").SetValue(allyNode, ally.Creature);
        AccessTools.Property(typeof(NCreature), "Visuals").SetValue(allyNode, allyRig);
        allyNode.AddChild(allyRig);
        actor.GetParent().AddChild(allyNode);
        AimActors.Add(ally.Creature, allyNode);
        try
        {
            combat.Player.PlayerCombatState!.ResetEnergy();
            Call("Reset");
            actor.Position = Vector2.Zero;
            anchor.Transform = Transform2D.Identity;
            Face(false);
            target.Position = new(700f, 0f);
            targetCenter.Position = new(0f, -500f);
            void Drag(Vector2 pointer, Creature? hovered = null) => Call("Drag", drag, card, pointer, hovered);
            Drag(targetCenter.GlobalPosition, combat.Enemy);
            float upper = Angle();
            Drag(secondCenter.GlobalPosition, second);
            float lower = Angle();
            Require(upper < -.01f && lower > .01f,
                $"The aiming fixture must span upright: upper={upper}, lower={lower}, legal={card.CanPlayTargeting(combat.Enemy)}.");
            Drag(new(700f, -3000f));
            Require(Math.Abs(Angle() - upper) < .001f, "Aim escaped the highest legal target core.");
            Drag(new(700f, 3000f));
            Require(Math.Abs(Angle() - lower) < .001f, "Aim escaped the lowest legal target core.");
            second.SetCurrentHpInternal(0);
            Call("SyncNow");
            Require(Math.Abs(Angle()) < .001f, "A dead lower target retained its aiming range.");
            Drag(new(-700f, -3000f));
            Tick(.15f);
            Require(anchor.Scale.X < 0 && Math.Abs(Angle()) < .001f, "An empty side must face left without tilting.");
            second.SetCurrentHpInternal(1000);
            secondNode.Position = new(-700f, 0f);
            Drag(secondCenter.GlobalPosition, second);
            Vector2 forward = pose.GetGlobalTransformWithCanvas().BasisXform(Vector2.Right).Normalized();
            Vector2 toTarget = (secondCenter.GlobalPosition - rig.VfxSpawnPosition.GlobalPosition).Normalized();
            Require(forward.Dot(toTarget) > .999f, "Left-side locked aiming missed its actual core.");

            Call("Reset");
            Face(false);
            Drag(new(-700f, -300f));
            Drag(new(700f, -300f));
            Require(AccessTools.Field(type, "_turnProjection").GetValue(pose) == null,
                "A same-frame reversal retained an idle projection owner.");
            Drag(new(-700f, -300f));
            Tick(.05f);
            Transform2D beforeReverse = source.Transform;
            float angleBeforeReverse = (float)AccessTools.Field(type, "_turnAngle").GetValue(pose)!;
            Drag(new(700f, -300f));
            Require(source.Transform.IsEqualApprox(beforeReverse), "Retargeting reset the turn projection.");
            Require((float)AccessTools.Field(type, "_turnAngle").GetValue(pose)! == angleBeforeReverse,
                "Retargeting jumped to a new half-turn origin.");
            Tick(.15f);
            Require(anchor.Scale.X > 0 && !((bool)AccessTools.Property(type, "OwnsSpin").GetValue(pose)!),
                "A reversed turn did not settle directly into the latest direction.");
            Drag(new(-700f, -300f));
            Tick(.05f);
            Call("BeginAction", combat.Enemy, false, false);
            Call("SetTravel", new Vector2(90f, 0f), 1f);
            Require(((Vector2)AccessTools.Property(type, "Travel").GetValue(pose)!).X == 90f && anchor.Scale.X > 0,
                "An attack waited for a drag turn or kept its obsolete direction.");
            Require(actor.Position.IsZeroApprox(), "An attack displaced the creature UI root during a drag turn.");
            Call("Reset");
            actor.Position = Vector2.Zero;

            var allyCard = combat.State.CreateCard<BelieveInYou>(combat.Player);
            Face(false);
            Require(allyCard.CanPlayTargeting(ally.Creature) && !allyCard.CanPlayTargeting(combat.Enemy),
                "The friendly targeting fixture is not using native target legality.");
            Call("Drag", drag, allyCard, allyCenter.GlobalPosition, ally.Creature);
            float allyAngle = Angle();
            Call("Drag", drag, allyCard, new Vector2(700f, -3000f), null);
            Require(Math.Abs(Angle() - allyAngle) < .001f, "Friendly aiming used enemy cores to expand its range.");
            Require(allyCard.CanPlayTargeting(combat.Player.Creature), "The self-target fixture must allow the owner.");
            Call("Drag", drag, allyCard, rig.VfxSpawnPosition.GlobalPosition, combat.Player.Creature);
            Require(Math.Abs(Angle()) < .001f, "A self-targeted card tilted its owner.");
            Call("Reset");

            var contour = (System.Numerics.Vector2[])AccessTools.Field(assembly.GetType("NinjaSlayer.Code.Combat.CombatBodyContours"),
                form == 2 ? "FullyReleasedNaraku" : "NinjaSlayer").GetValue(null)!;
            void Energy(int value)
            {
                combat.Player.PlayerCombatState!.LoseEnergy(combat.Player.PlayerCombatState.Energy);
                combat.Player.PlayerCombatState.GainEnergy(value);
            }
            foreach (int energy in new[] { 0, 1, 3 })
            {
                Energy(energy);
                Require(!tornado.ShouldGlowGold, "A low-X Tornado unexpectedly glowed gold.");
                Call("Drag", drag, tornado, new Vector2(-700f, -3000f), second);
                for (int frame = 0; frame < 60; frame++) Tick(1f / 60f);
                Require(pose.Transform.IsEqualApprox(Transform2D.Identity)
                    && ((Vector2)AccessTools.Property(type, "Travel").GetValue(pose)!).IsZeroApprox(),
                    "A non-glowing Tornado charged or aimed at the pointer.");
                Call("Reset");
            }
            var chemicalX = MegaCrit.Sts2.Core.Models.ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.ChemicalX>().ToMutable();
            combat.Player.AddRelicInternal(chemicalX);
            try
            {
                Energy(2);
                Call("Drag", drag, tornado, new Vector2(700f, -300f), null);
                Tick(.01f);
                Require(tornado.ShouldGlowGold && (bool)AccessTools.Field(type, "_charging").GetValue(pose)!,
                    "Chemical X's two extra hits did not enable both gold glow and charging.");
                Call("Reset");
            }
            finally { combat.Player.RemoveRelicInternal(chemicalX); }
            Energy(4);
            Call("Drag", drag, tornado, new Vector2(-700f, -3000f), second);
            Tick(.3f);
            Require(tornado.ShouldGlowGold && (bool)AccessTools.Field(type, "_charging").GetValue(pose)!,
                "Four-X glow and charge did not agree.");
            Energy(3);
            Tick(.001f);
            Require(!(bool)AccessTools.Field(type, "_charging").GetValue(pose)!, "Losing gold glow did not stop trembling.");
            await ToSignal(GetTree().CreateTimer(.25f), SceneTreeTimer.SignalName.Timeout);
            Require(pose.Transform.IsEqualApprox(Transform2D.Identity), "Losing gold glow retained charge compression.");
            Energy(4);
            Tick(.1f);
            Energy(3);
            Tick(.001f);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Transform2D halfway = pose.Transform;
            Energy(4);
            Tick(0f);
            Require(pose.Transform.IsEqualApprox(halfway), "Regaining gold glow snapped the returning charge pose.");
            Call("Reset");
            Energy(4);
            foreach (var (name, values) in ChargePreviews)
            foreach (float altitude in new[] { 0f, -150f })
            foreach (bool left in new[] { false, true })
            {
                profile.SetValue(pose, Activator.CreateInstance(profile.PropertyType, values.Cast<object>().ToArray()));
                anchor.Transform = new Transform2D(0f, new Vector2(0f, altitude));
                Face(left);
                Transform2D uncharged = source.Transform;
                Call("Drag", drag, tornado, new Vector2(-700f, -3000f), second);
                for (int frame = 0; frame < 240; frame++)
                {
                    Tick(1f / 60f);
                    Sprite2D active = overlay.Visible ? overlay : source;
                    float bottom = contour.Max(p => (active.GetGlobalTransformWithCanvas() * new Vector2(p.X, p.Y)).Y);
                    float ground = altitude - 6.45f;
                    Require(Math.Abs(bottom - ground) < .5f, $"{name} charge lifted or buried the feet in form {form}.");
                    Require((anchor.Scale.X < 0) == left && Math.Abs(Angle()) < .001f,
                        "Tornado charge followed pointer direction or tilt.");
                    Require(source.Transform.IsEqualApprox(uncharged), "Tornado charge started a body spin.");
                    VerifyAimedGroundShadow(rig, form);
                    if (frame % 20 == 0)
                    {
                        Type hand = assembly.GetType("NinjaSlayer.Code.Nodes.ShurikenOrbVisual", true)!;
                        object?[] handArgs = [actor, null];
                        Require((bool)AccessTools.Method(hand, "TryGetHandCanvasPosition").Invoke(null, handArgs)!,
                            "Charging lost the held shuriken anchor.");
                        Vector2 point = form == 2 ? new(553f, 163f) : new(854f, -110f);
                        Require(((Vector2)handArgs[1]!).DistanceTo(active.GetGlobalTransformWithCanvas() * point) < .1f,
                            "The shuriken did not inherit charge squash and trembling.");
                    }
                }
                ProcessModeEnum processMode = rig.ProcessMode;
                Transform2D paused = pose.Transform;
                rig.ProcessMode = ProcessModeEnum.Disabled;
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Require(pose.Transform.IsEqualApprox(paused), "Paused charging changed its transform during PreDraw.");
                rig.ProcessMode = processMode;
                Call("EndDrag", drag, true);
                Call("CancelPendingCharge", card);
                Require((bool)AccessTools.Field(type, "_chargePending").GetValue(pose)!,
                    "Cancelling another card released the queued Tornado charge.");
                Call("CancelPendingCharge", tornado);
                await ToSignal(GetTree().CreateTimer(.25f), SceneTreeTimer.SignalName.Timeout);
                Require(pose.Transform.IsEqualApprox(Transform2D.Identity) && source.Transform.IsEqualApprox(uncharged),
                    $"{name} zero-hit cleanup retained a compressed body.");
                Call("Drag", drag, tornado, new Vector2(-700f, -3000f), second);
                Tick(.01f);
                Call("EndDrag", drag, true);
                Call("BeginTornado", combat.Enemy, false, true);
                Call("SetTravel", new Vector2(120f, 0f), 1f);
                Vector2 scale = (Vector2)AccessTools.Field(type, "_chargeScale").GetValue(pose)!;
                Require(scale.IsEqualApprox(Vector2.One), "Rapid release retained charge compression at impact.");
                Call("BeginReturn");
                Call("ApplyReturn", 1f);
                Call("Reset");
                actor.Position = Vector2.Zero;
            }
            var speedBefore = MegaCrit.Sts2.Core.Saves.SaveManager.Instance.PrefsSave.FastMode;
            foreach (var mode in new[] { MegaCrit.Sts2.Core.Settings.FastModeType.Normal, MegaCrit.Sts2.Core.Settings.FastModeType.Fast })
            foreach (bool empowered in new[] { false, true })
            {
                MegaCrit.Sts2.Core.Saves.SaveManager.Instance.PrefsSave.FastMode = mode;
                Call("BeginTornado", combat.Enemy, false, empowered);
                Tick(.01f);
                float expected = empowered ? 120f : 48f;
                Require(Math.Abs((float)AccessTools.Field(type, "_spinDegrees").GetValue(pose)! - expected) < .01f,
                    "Tornado started at the wrong tier's rotation speed.");
                Call("PauseTornadoSpin", .035f);
                Tick(.02f);
                Require(Math.Abs((float)AccessTools.Field(type, "_spinDegrees").GetValue(pose)! - expected) < .01f,
                    "Visual hit-stop advanced its rotation clock.");
                Tick(.025f);
                Require(Math.Abs((float)AccessTools.Field(type, "_spinDegrees").GetValue(pose)! - expected * 2f) < .01f,
                    "Rotation caught up the paused time instead of resuming from the held phase.");
                foreach (int fps in new[] { 30, 60, 120, 144 })
                {
                    Call("PauseTornadoSpin", .05f);
                    pose._Process(1d / fps);
                    var exposure = (Func<double, double>)AccessTools.Field(type, "_spinExposure").GetValue(pose)!;
                    Require(double.IsFinite(exposure(0d)) && double.IsFinite(exposure(.04d)),
                        "A fully paused render frame produced an invalid exposure range.");
                }
                Call("Reset");
            }
            MegaCrit.Sts2.Core.Saves.SaveManager.Instance.PrefsSave.FastMode = speedBefore;
            profile.SetValue(pose, savedProfile);
            anchor.Transform = Transform2D.Identity;
            Face(false);
            pose.SetProcess(true);
            SoarSpinAnimation.StartAirborneSpin(combat.Player.Creature, 2400f);
            Drag(new(-700f, -300f));
            await ToSignal(GetTree().CreateTimer(.22f), SceneTreeTimer.SignalName.Timeout);
            Require(anchor.Scale.X < 0 && source.Scale.IsFinite(), "A drag turn failed during persistent spin.");
            Require(source.Visible != overlay.Visible, "Composed turning drew two body sprites.");
            SoarSpinAnimation.ResetSpinVisual(combat.Player.Creature);
            Call("Reset");
            Require(Math.Abs(source.Scale.X - .33f) < .001f, "Composed spin cleanup retained projected body width.");
            pose.SetProcess(false);
            GD.Print($"PASS drag controls form {form}: legal side envelopes, dead target update, empty side, friendly/self aim, turn reversal/action takeover, grounded charge profiles, zero-hit and rapid release.");
            if (form == 0 && System.Environment.GetEnvironmentVariable("NINJASLAYER_DRAG_RENDER_DIR") != null)
                await RenderDragPreviews(combat, actor, target, targetCenter, rig, second, secondNode, secondCenter, drag);
        }
        finally
        {
            SoarSpinAnimation.ResetSpinVisual(combat.Player.Creature);
            Call("Reset");
            profile.SetValue(pose, savedProfile);
            pose.SetProcess(processing);
            actor.Position = actorBefore;
            anchor.Transform = anchorBefore;
            target.Position = targetBefore;
            targetCenter.Position = targetCenterBefore;
            AimActors.Remove(second);
            AimActors.Remove(ally.Creature);
            combat.State.RemoveCreature(second);
            combat.State.RemoveCreature(ally.Creature);
            secondNode.Free();
            allyNode.Free();
            drag.Free();
        }
    }
}
