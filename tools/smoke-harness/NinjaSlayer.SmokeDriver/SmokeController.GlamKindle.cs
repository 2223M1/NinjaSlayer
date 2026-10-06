using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.Unlocks;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Content;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private async Task RunGlamKindleAsync()
    {
        SaveManager.Instance.SetFtuesEnabled(false);
        foreach (FastModeType speed in new[] { FastModeType.Normal, FastModeType.Fast })
        {
            SaveManager.Instance.PrefsSave.FastMode = speed;
            var local = Player.CreateForNewRun<NinjaSlayerCharacter>(UnlockState.all, 1);
            var remote = Player.CreateForNewRun<NinjaSlayerCharacter>(UnlockState.all, 2);
            var native = Player.CreateForNewRun<Ironclad>(UnlockState.all, 3);
            var run = RunState.CreateForNewRun([local, remote, native], ActModel.GetDefaultList().Select(a => a.ToMutable()).ToList(),
                [], GameMode.Standard, 0, _configuration.Seed);
            RunManager.Instance.SetUpTest(run, new NetSingleplayerGameService(), true, false);
            AccessTools.Method(typeof(RunManager), "GenerateRooms").Invoke(RunManager.Instance, null);
            await (Task)AccessTools.Method(typeof(NGame), "StartRun").Invoke(NGame.Instance, [run])!;
            await RunManager.Instance.EnterAct(0);
            await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<GremlinMercNormal>().ToMutable());
            await WaitUntilAsync(() => run.Players.All(p => p.PlayerCombatState?.Phase == PlayerTurnPhase.Play), "Glam party fixture did not start.");
            var combat = local.Creature.CombatState!;
            foreach (var enemy in combat.HittableEnemies) { enemy.SetMaxHpInternal(10000); enemy.SetCurrentHpInternal(10000); }
            foreach (Player caster in new[] { remote, local })
            foreach (bool upgraded in new[] { false, true })
            foreach (bool enchanted in new[] { false, true })
            foreach (bool shortPile in new[] { false, true })
            {
                foreach (Player player in run.Players)
                {
                    foreach (var old in player.Piles.Where(p => p.Type != PileType.Deck).SelectMany(p => p.Cards).ToArray()) await CardPileCmd.RemoveFromCombat(old);
                    foreach (var power in player.Creature.Powers.ToArray()) await PowerCmd.Remove(power);
                    player.PlayerCombatState!.GainEnergy(20);
                    for (int i = 0; i < (shortPile ? upgraded ? 3 : 2 : 12); i++) await CardPileCmd.Add(combat.CreateCard<DefendIronclad>(player), PileType.Draw);
                }
                Player teammate = caster == local ? remote : local;
                var kindle = combat.CreateCard<Kindle>(caster);
                if (upgraded) CardCmd.Upgrade(kindle);
                if (enchanted) CardCmd.Enchant<Glam>(kindle, 1);
                var followup = combat.CreateCard<StrikeNinjaSlayer>(teammate);
                await CardPileCmd.Add(kindle, PileType.Hand);
                await CardPileCmd.Add(followup, PileType.Hand);
                await WaitFrames(20);
                var casterNode = caster.Creature.GetCreatureNode()!;
                var mateNode = teammate.Creature.GetCreatureNode()!;
                Vector2 casterRoot = casterNode.Position, mateRoot = mateNode.Position;
                Transform2D casterPose = casterNode.Visuals.Transform, matePose = mateNode.Visuals.Transform;
                var action = new PlayCardAction(kindle, null);
                var other = new PlayCardAction(followup, combat.HittableEnemies[0]);
                RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(action);
                RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(other);
                await Task.WhenAll(action.CompletionTask, other.CompletionTask).WaitAsync(TimeSpan.FromSeconds(15));
                if (action.Exception != null) throw action.Exception;
                if (other.Exception != null) throw other.Exception;
                var nativePower = combat.CreateCard<Inflame>(native);
                await CardPileCmd.Add(nativePower, PileType.Hand);
                var nativeAction = new PlayCardAction(nativePower, null);
                RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(nativeAction);
                await nativeAction.CompletionTask.WaitAsync(TimeSpan.FromSeconds(15));
                if (nativeAction.Exception != null) throw nativeAction.Exception;
                var matePower = combat.CreateCard<Resilience>(teammate);
                await CardPileCmd.Add(matePower, PileType.Hand);
                var mateAction = new PlayCardAction(matePower, null);
                RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(mateAction);
                await mateAction.CompletionTask.WaitAsync(TimeSpan.FromSeconds(15));
                if (mateAction.Exception != null) throw mateAction.Exception;
                int plays = enchanted ? 2 : 1;
                int expectedHand = shortPile && enchanted ? (upgraded ? 3 : 2) + 1 : plays * (upgraded ? 3 : 2);
                Require(PileType.Hand.GetPile(caster).Cards.Count == expectedHand
                    && caster.PlayerCombatState!.AllCards.OfType<BlackFlame>().Count() == plays
                    && kindle.Pile?.Type == PileType.Discard && followup.Pile?.Type == PileType.Discard,
                    "Glam Kindle changed repeated draw, generation ownership or teammate action completion.");
                await WaitFrames(90);
                Require(casterNode.Position.IsEqualApprox(casterRoot) && mateNode.Position.IsEqualApprox(mateRoot)
                    && casterNode.Visuals.Transform.IsEqualApprox(casterPose) && mateNode.Visuals.Transform.IsEqualApprox(matePose),
                    "Glam Kindle left a caster/teammate body animation unfinished.");
                _checkpoints.Write("glam-kindle.card", data: new JsonObject { ["ownerLocal"] = caster == local,
                    ["upgraded"] = upgraded, ["glam"] = enchanted, ["speed"] = speed.ToString(), ["plays"] = plays, ["shortPile"] = shortPile });
            }
            foreach (var player in run.Players) PlayerCmd.EndTurn(player, true);
            await WaitUntilAsync(() => local.PlayerCombatState!.TurnNumber >= 2 && local.PlayerCombatState.Phase == PlayerTurnPhase.Play,
                "Glam Kindle party turn could not advance.");
            await NGame.Instance!.ReturnToMainMenuAfterRun();
            await WaitFrames(30);
        }
        _checkpoints.Write("glam-kindle.completed");
        NGame.Instance!.Quit();
    }
}
