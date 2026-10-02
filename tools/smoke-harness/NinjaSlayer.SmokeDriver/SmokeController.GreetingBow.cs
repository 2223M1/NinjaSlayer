using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using NinjaSlayer.Content;
using NinjaSlayer.Monsters;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private async Task RunGreetingBowAsync()
    {
        SaveManager.Instance.SetFtuesEnabled(false);
        var run = await NGame.Instance!.StartNewSingleplayerRun(
            ModelDb.Character<NinjaSlayerCharacter>(), true, ActModel.GetDefaultList(),
            [], _configuration.Seed, GameMode.Standard, 0);
        await RunManager.Instance.EnterAct(0);
        await RunManager.Instance.EnterMapCoord(run.Map.GetAllMapPoints().First(p => p.PointType == MapPointType.Monster).coord);
        var player = LocalContext.GetMe(run)!;
        await WaitUntilAsync(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play, "Bow fixture did not start.");
        var combat = player.Creature.CombatState!;
        var assembly = typeof(NinjaSlayerCharacter).Assembly;
        var bowType = assembly.GetType("NinjaSlayer.Code.ExternalAnimations.GreetingBow", true)!;
        var facingType = assembly.GetType("NinjaSlayer.Code.Nodes.CombatFacingTurn", true)!;
        foreach (var model in new MonsterModel[] { ModelDb.Monster<DarkNinjaMonster>(), ModelDb.Monster<ForestSawatariMonster>() })
        {
            var creature = combat.CreateCreature(model.ToMutable(), CombatSide.Enemy, null);
            await CreatureCmd.Add(creature);
            await WaitFrames(60);
            var node = NCombatRoom.Instance!.GetCreatureNode(creature)!;
            var anchor = node.Visuals.GetNode<Node2D>("AirborneAnchor");
            var body = (Sprite2D)node.Body;
            var controller = AccessTools.Method(facingType, "Ensure").Invoke(null, [node]);
            foreach (bool left in new[] { true, false })
            foreach (float scale in new[] { 1f, 1.4f })
            {
                node.Scale = Vector2.One * scale;
                AccessTools.Method(facingType, "SetFacing").Invoke(controller, [left, true]);
                await WaitFrames(6);
                var baseline = anchor.Transform;
                var rootBefore = node.GetGlobalTransform();
                var health = node.GetNode<Control>("%HealthBar");
                var healthBefore = health.GetGlobalTransform();
                var intentBefore = node.IntentContainer.GetGlobalTransform();
                Vector2 head = new(0f, body.GetRect().Position.Y);
                var before = body.GetGlobalTransformWithCanvas() * head;
                using (var bow = (IDisposable)AccessTools.GetDeclaredConstructors(bowType).Single().Invoke([creature]))
                {
                    AccessTools.Method(bowType, "Apply").Invoke(bow, [.35f]);
                    await WaitFrames(2);
                    var after = body.GetGlobalTransformWithCanvas() * head;
                    float dx = after.X - before.X;
                    _checkpoints.Write("greetingbow.sample", data: new JsonObject { ["model"] = model.Id.ToString(), ["left"] = left, ["scale"] = scale, ["headDeltaX"] = dx });
                    Require(left ? dx < -1f : dx > 1f, "Greeting head leaned away from the facing direction.");
                    Require(node.GetGlobalTransform().IsEqualApprox(rootBefore), "Greeting moved the creature root.");
                    Require(health.GetGlobalTransform().IsEqualApprox(healthBefore), "Greeting moved the health display.");
                    Require(node.IntentContainer.GetGlobalTransform().IsEqualApprox(intentBefore), "Greeting moved the intent display.");
                    // Dispose while held verifies the same restoration used by skips and cancellation.
                }
                Require(anchor.Transform.IsEqualApprox(baseline), "Interrupted bow did not restore its original body transform.");
                using (var bow = (IDisposable)AccessTools.GetDeclaredConstructors(bowType).Single().Invoke([creature]))
                {
                    AccessTools.Method(bowType, "Apply").Invoke(bow, [1f]);
                    Require(anchor.Transform.IsEqualApprox(baseline), "Completed bow did not return to its original pose.");
                }
            }
        }
        _checkpoints.Write("greetingbow.completed");
        NGame.Instance!.Quit();
    }
}
