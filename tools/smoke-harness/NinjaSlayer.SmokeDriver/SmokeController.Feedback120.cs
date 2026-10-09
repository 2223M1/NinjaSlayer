using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using NinjaSlayer.Cards;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Content;
using NinjaSlayer.Monsters;
using NinjaSlayer.Relics;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private async Task VerifyFeedback120()
    {
        var choice = new BlockingPlayerChoiceContext();
        foreach (FastModeType speed in new[] { FastModeType.Normal, FastModeType.Fast, FastModeType.Instant })
        {
            SaveManager.Instance.PrefsSave.FastMode = speed;
            var run = await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<NinjaSlayerCharacter>(), true,
                ActModel.GetDefaultList(), [], _configuration.Seed, GameMode.Standard, 0);
            var player = run.Players[0];
            await RelicCmd.Obtain<OrigamiPactRelic>(player);
            await RunManager.Instance.EnterAct(1);
            await RunManager.Instance.EnterRoomDebug(RoomType.Boss, model: ModelDb.Encounter<TheInsatiableBoss>().ToMutable());
            await WaitUntilAsync(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play,
                "Feedback 1.0.20 fixture did not begin.");
            await WaitFrames(90);
            var combat = player.Creature.CombatState!;
            var actor = player.Creature.GetCreatureNode()!;
            var koki = player.PlayerCombatState!.Pets.Single(p => p.Monster is YamotoKokiMonster);
            var companion = koki.GetCreatureNode()!;
            Vector2 initialSpacing = companion.GlobalPosition - actor.GlobalPosition;
            Vector2 root = actor.Position, visual = actor.Visuals.Position;
            float initialGlobalX = actor.GlobalPosition.X;
            var attacks = new List<Task>();
            for (int i = 0; i < 12; i++)
            {
                attacks.Add(CreatureCmd.TriggerAnim(player.Creature, "Attack", .1f));
                await _tree.ToSignal(_tree.CreateTimer(.03), SceneTreeTimer.SignalName.Timeout);
            }
            await Task.WhenAll(attacks);
            await _tree.ToSignal(_tree.CreateTimer(1.2), SceneTreeTimer.SignalName.Timeout);
            Require(actor.Position.DistanceTo(root) < .1f && actor.Visuals.Position.DistanceTo(visual) < .1f,
                $"Repeated native Attack drifted in {speed}: root {actor.Position - root}, visual {actor.Visuals.Position - visual}.");
            _checkpoints.Write("feedback120.attack-return." + speed);

            await CardPileCmd.Add(combat.CreateCard<Machete>(player), PileType.Hand);
            for (int i = 0; i < 3; i++)
            {
                var ready = combat.CreateCard<ReadyShuriken>(player);
                await CardPileCmd.Add(ready, PileType.Hand);
                await CardCmd.AutoPlay(choice, ready, player.Creature);
            }
            await WaitFrames(45);
            Require(actor.FindChild("PlayerMachetes", true, false) is Node2D { Visible: true },
                "Held-machete fixture is missing.");
            if (!_configuration.NoScreenshots)
            {
                await CapturePresentation("feedback120-held-shuriken-" + speed);
                NCardPileScreen.ShowScreen(PileType.Draw.GetPile(player), []);
                await WaitFrames(30);
                await CapturePresentation("feedback120-shuriken-under-pile-" + speed);
                NCapstoneContainer.Instance!.Close();
                await WaitFrames(20);
            }
            _checkpoints.Write("feedback120.shuriken-pile." + speed);

            var enemy = combat.Enemies.Single();
            await ((MoveState)enemy.Monster!.MoveStateMachine!.States["LIQUIFY_GROUND_MOVE"])
                .PerformMove([player.Creature]);
            // Liquify and the next turn's decrement are separate native actions.
            // Instant mode skips Cmd.Wait, so let the first native tween finish
            // before this fixture invokes the next turn's power change directly.
            await _tree.ToSignal(_tree.CreateTimer(.5), SceneTreeTimer.SignalName.Timeout);
            var sandpit = enemy.Powers.OfType<SandpitPower>().Single();
            await PowerCmd.Decrement(sandpit);
            await WaitFrames(30);
            float pulledX = actor.GlobalPosition.X;
            _checkpoints.Write("feedback120.sandpit-before-spawn." + speed, data: new System.Text.Json.Nodes.JsonObject
                { ["initialGlobalX"] = initialGlobalX, ["pulledX"] = pulledX, ["amount"] = sandpit.Amount });
            Require(pulledX > initialGlobalX + 5, "Native Sandpit did not pull the player right.");
            Require((companion.GlobalPosition - actor.GlobalPosition).DistanceTo(initialSpacing) < .5f,
                "Initial Sandpit pull changed companion spacing.");
            await ((MoveState)koki.Monster!.MoveStateMachine!.States[YamotoKokiMonster.SummonMissileMoveId])
                .PerformMove(combat.Enemies);
            await WaitFrames(30);
            Require(Math.Abs(actor.GlobalPosition.X - pulledX) < .5f,
                $"Adding origami missiles changed Sandpit's player position: {pulledX} -> {actor.GlobalPosition.X}; amount {sandpit.Amount}.");
            Require((companion.GlobalPosition - actor.GlobalPosition).DistanceTo(initialSpacing) < .5f,
                $"Adding missiles changed companion spacing: {initialSpacing} -> {companion.GlobalPosition - actor.GlobalPosition}.");
            var missiles = player.PlayerCombatState.Pets.Where(p => p.Monster is OrigamiMissileMonster).ToArray();
            Require(missiles.Length >= 2, "The native Yamoto move did not spawn missiles.");
            await CreatureCmd.Kill(missiles, force: true);
            await WaitFrames(45);
            Require(Math.Abs(actor.GlobalPosition.X - pulledX) < .5f,
                "Removing origami missiles overwrote Sandpit's player position.");
            Require((companion.GlobalPosition - actor.GlobalPosition).DistanceTo(initialSpacing) < .5f,
                "Removing missiles changed companion spacing.");
            await PowerCmd.Decrement(sandpit);
            await WaitFrames(30);
            Require(actor.GlobalPosition.X > pulledX + 5, "Sandpit stopped pulling after companion changes.");
            Require((companion.GlobalPosition - actor.GlobalPosition).DistanceTo(initialSpacing) < .5f,
                "The next Sandpit pull changed companion spacing.");
            if (!_configuration.NoScreenshots) await CapturePresentation("feedback120-sandpit-" + speed);
            _checkpoints.Write("feedback120.sandpit-companions." + speed, data: new System.Text.Json.Nodes.JsonObject
                { ["initialSpacingX"] = initialSpacing.X, ["finalSpacingX"] = companion.GlobalPosition.X - actor.GlobalPosition.X,
                  ["playerMovementX"] = actor.GlobalPosition.X - initialGlobalX });
            await NGame.Instance.ReturnToMainMenuAfterRun();
            await WaitFrames(30);
        }
    }
}
