using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Cards;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using NinjaSlayer.Relics;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private async Task VerifyPlayerWeaponForms(string directory)
    {
        var combat = CombatManager.Instance.DebugOnlyGetState()!;
        var player = LocalContext.GetMe(combat.RunState)!;
        var actor = player.Creature.GetCreatureNode()!;
        var choice = new BlockingPlayerChoiceContext();
        var rig = JsonNode.Parse(File.ReadAllText(Path.Combine(directory,
            "../../source-reconstruction/sawatari-weapons/player-grip-review-02/player-weapon-rig.json")))!;
        Type facing = typeof(SawatariMachete).Assembly.GetType("NinjaSlayer.Code.ExternalAnimations.NinjaSlayerFacingState", true)!;
        Vector2 Point(JsonNode node) => new(node[0]!.GetValue<float>(), node[1]!.GetValue<float>());
        foreach (string form in new[] { "normal", "semi-naraku", "naraku", "one-soul" })
        {
            NarakuWithinRelic? relic = null;
            if (form is "semi-naraku" or "naraku")
                await PowerCmd.Apply<NarakuFormRedesignPower>(choice, player.Creature, 1, player.Creature, null);
            if (form == "naraku") relic = await RelicCmd.Obtain<NarakuWithinRelic>(player);
            if (form == "one-soul") await PowerCmd.Apply<OneBodyOneSoulPower>(choice, player.Creature, 1, player.Creature, null);
            var first = combat.CreateCard<SawatariMachete>(player);
            var second = combat.CreateCard<SawatariMachete>(player);
            foreach (bool mirrored in new[] { false, true })
            {
                AccessTools.Method(facing, "SetFacing").Invoke(null, [actor, mirrored]);
                for (int count = 0; count <= 2; count++)
                {
                    if (count == 1) await CardPileCmd.Add(first, PileType.Hand);
                    if (count == 2) await CardPileCmd.Add(second, PileType.Hand);
                    await WaitFrames(20);
                    var visual = (Node2D)actor.Visuals.FindChild("PlayerMachetes", true, false)!;
                    Sprite2D source = actor.Visuals.GetNode<Sprite2D>("%Visuals");
                    Sprite2D? overlay = actor.Visuals.FindChild("NarakuVisualOverlay", true, false) as Sprite2D;
                    Sprite2D body = overlay is { Visible: true } ? overlay : source;
                    var knives = visual.FindChildren("Machete", "Sprite2D", true, false).Cast<Sprite2D>().Where(n => n.IsVisibleInTree()).ToArray();
                    Require(knives.Length == count, $"{form}/{mirrored}: wrong visible held-knife count.");
                    foreach (Sprite2D knife in knives)
                    {
                        string hand = knife.GetParent().Name == "Primary" ? "primary" : "secondary";
                        JsonNode approved = rig["forms"]![form]!["hands"]![hand]!;
                        Vector2 point = Point(approved["position_centered"]!);
                        if (body.FlipH) point.X *= -1;
                        if (body.FlipV) point.Y *= -1;
                        point += body.Offset;
                        Require(body.ToGlobal(point).DistanceTo(knife.GlobalPosition) < .2f,
                            $"{form}/{mirrored}/{hand}: handle missed the approved grip.");
                        Require(DrawsBefore(body, knife)
                            && DrawsBefore(visual.GetNode<Node2D>("Secondary"), visual.GetNode<Node2D>("Primary"))
                            && DrawsBefore(knife, NCombatRoom.Instance!.GetNode<CanvasItem>("CombatUi")),
                            "Held blades must preserve body/hand order below combat UI.");
                        Transform2D local = body.GlobalTransform.AffineInverse() * knife.GlobalTransform;
                        Vector2 expectedX = Vector2.FromAngle(Mathf.DegToRad(approved["node_rotation_degrees"]!.GetValue<float>()));
                        expectedX *= approved["scale_body_local"]!.GetValue<float>();
                        expectedX *= (.52f * (hand == "primary" ? 1.5756349f : 1.5616022f)) / .65f;
                        Vector2 expectedY = new(expectedX.Y, -expectedX.X);
                        if (body.FlipH) { expectedX.X *= -1; expectedY.X *= -1; }
                        if (body.FlipV) { expectedX.Y *= -1; expectedY.Y *= -1; }
                        Require(local.X.DistanceTo(expectedX) < .002f && local.Y.DistanceTo(expectedY) < .002f,
                            $"{form}/{mirrored}/{hand}: blade does not preserve the source size and approved grip angle.");
                    }
                    SaveScreenshot(Path.Combine(directory, $"player-{form}-{mirrored}-{count}.png"));
                    _checkpoints.Write($"sawatari.player-weapons.{form}.{mirrored}.{count}");
                }
                await CardCmd.Exhaust(choice, first);
                await CardCmd.Exhaust(choice, second);
            }
            if (relic != null) await RelicCmd.Remove(relic);
            if (form is "semi-naraku" or "naraku") await PowerCmd.Remove<NarakuFormRedesignPower>(player.Creature);
            if (form == "one-soul") await PowerCmd.Remove<OneBodyOneSoulPower>(player.Creature);
        }
        AccessTools.Method(facing, "SetFacing").Invoke(null, [actor, false]);
        _checkpoints.Write("sawatari.player-review-02-verified");

        Type weapons = typeof(SawatariMachete).Assembly.GetType("NinjaSlayer.Code.Nodes.SawatariWeaponVisuals", true)!;
        var projectile = (Sprite2D)AccessTools.Method(weapons, "CreateMachete").Invoke(null, null)!;
        var destination = new Node2D { Position = new Vector2(900, 300) };
        var container = NCombatRoom.Instance!.CombatVfxContainer;
        container.AddChild(projectile);
        container.AddChild(destination);
        projectile.Position = new Vector2(300, 300);
        ulong identity = projectile.GetInstanceId();
        Task<bool> flight = (Task<bool>)AccessTools.Method(weapons, "FlyWeapon")
            .Invoke(null, [projectile, destination, true, 177.9726773f, 32f, .25f, false])!;
        await WaitFrames(2);
        Require(projectile.GetInstanceId() == identity && projectile.Position.X > 300,
            "Weapon flight did not move its existing node.");
        destination.QueueFree();
        Require(!await flight, "A disappearing receiving grip was reported as a completed catch.");
        await WaitFrames(2);
        Require(!GodotObject.IsInstanceValid(projectile), "Interrupted weapon flight left a detached sprite.");
        _checkpoints.Write("sawatari.interrupted-flight-cleaned");
    }
}
