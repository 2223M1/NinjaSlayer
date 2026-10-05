using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using NinjaSlayer.Events;
using NinjaSlayer.Powers;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private async Task RunRelease114Async()
    {
        SaveManager.Instance.SetFtuesEnabled(false);
        foreach (FastModeType speed in new[] { FastModeType.Normal, FastModeType.Fast })
        {
            SaveManager.Instance.PrefsSave.FastMode = speed;
            var local = Player.CreateForNewRun<NinjaSlayerCharacter>(UnlockState.all, 1);
            var remote = Player.CreateForNewRun<NinjaSlayerCharacter>(UnlockState.all, 2);
            var third = Player.CreateForNewRun<Ironclad>(UnlockState.all, 3);
            var fourth = Player.CreateForNewRun<Ironclad>(UnlockState.all, 4);
            var run = RunState.CreateForNewRun([local, remote, third, fourth], ActModel.GetDefaultList().Select(a => a.ToMutable()).ToList(),
                [], GameMode.Standard, 0, _configuration.Seed);
            RunManager.Instance.SetUpTest(run, new NetSingleplayerGameService(), true, false);
            AccessTools.Method(typeof(RunManager), "GenerateRooms").Invoke(RunManager.Instance, null);
            await (Task)AccessTools.Method(typeof(NGame), "StartRun").Invoke(NGame.Instance, [run])!;
            await RunManager.Instance.EnterAct(0);
            await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<GremlinMercNormal>().ToMutable());
            await WaitUntilAsync(() => local.PlayerCombatState?.Phase == PlayerTurnPhase.Play, "Party health fixture did not start.");
            var choice = new BlockingPlayerChoiceContext();
            var panels = _tree.Root.FindChildren("*", "", true, false).OfType<NMultiplayerPlayerState>().ToArray();
            Require(panels.Any(panel => panel.Player == remote), "Native remote-player panel was not instantiated.");
            var remoteBar = panels.Single(panel => panel.Player == remote).GetNode<NHealthBar>("%HealthBar");
            foreach (Player player in new[] { local, remote })
            {
                await CreatureCmd.SetCurrentHp(player.Creature, player.Creature.MaxHp - 20);
                await PowerCmd.Apply<NarakuLifePower>(choice, player.Creature, 8, player.Creature, null);
                await CreatureCmd.GainBlock(player.Creature, 5, ValueProp.Unpowered, null);
                await CreatureCmd.SetCurrentHp(player.Creature, player.Creature.MaxHp - 22);
                await PowerCmd.ModifyAmount(choice, player.Creature.GetPower<NarakuLifePower>()!, 3, player.Creature, null);
            }
            var strip = remoteBar.GetNode<Control>("%PoisonForeground").GetParent().GetNode<NinePatchRect>("NinjaSlayerNarakuLifeStrip");
            Require(strip.Visible && remote.Creature.GetPowerAmount<NarakuLifePower>() == 11,
                "Remote panel must display embedded Naraku while native block/HP/power callbacks refresh it.");
            await PowerCmd.Remove(remote.Creature.GetPower<NarakuLifePower>()!);
            Require(!strip.Visible, "Remote strip must hide after native removal.");
            await PowerCmd.Apply<NarakuLifePower>(choice, remote.Creature, 5, remote.Creature, null);
            foreach (var player in run.Players) PlayerCmd.EndTurn(player, true);
            await WaitUntilAsync(() => local.PlayerCombatState!.TurnNumber >= 2
                && local.PlayerCombatState.Phase == PlayerTurnPhase.Play, "Party Naraku refresh stopped the native turn loop.");
            var card = local.Creature.CombatState!.CreateCard<DefendNinjaSlayer>(local);
            await CardPileCmd.Add(card, PileType.Hand);
            var action = new PlayCardAction(card, null);
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(action);
            await action.CompletionTask.WaitAsync(TimeSpan.FromSeconds(20));
            if (action.Exception != null) throw action.Exception;
            _checkpoints.Write("release114.naraku." + speed, data: new JsonObject { ["nativePlayerModels"] = 4, ["turn"] = local.PlayerCombatState!.TurnNumber });
            await NGame.Instance!.ReturnToMainMenuAfterRun();
            await WaitFrames(30);
        }
        var probe = new Harmony("NinjaSlayer.SmokeDriver.VisibleSawatariEntrance");
        probe.Patch(AccessTools.Method(typeof(AncientEntranceAnimation), nameof(AncientEntranceAnimation.Play), [typeof(Player)]),
            prefix: new HarmonyMethod(typeof(Release114EntranceProbe), nameof(Release114EntranceProbe.Prefix)));
        try
        {
            foreach (FastModeType speed in new[] { FastModeType.Normal, FastModeType.Fast })
            {
                SaveManager.Instance.PrefsSave.FastMode = speed;
                var run = await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<NinjaSlayerCharacter>(), true,
                    ActModel.GetDefaultList(), [], _configuration.Seed, GameMode.Standard, 0);
                await RunManager.Instance.EnterAct(0);
                Type route = typeof(TheMovingJungleEvent).Assembly.GetType("NinjaSlayer.Code.Patches.SawatariEventRoute", true)!;
                AccessTools.Method(route, "Schedule").Invoke(null, [run.Act, ModelDb.Encounter<GremlinMercNormal>()]);
                Require((bool)AccessTools.Method(route, "TryActivate").Invoke(null, [run.Act])!, "Sawatari event routing failed.");
                Release114EntranceProbe.Count = 0;
                await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<TheMovingJungleEvent>());
                var player = run.Players[0];
                await WaitUntilAsync(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play, "Visible event entrance did not start combat.");
                Require(Release114EntranceProbe.Count == 1 && player.Creature.GetCreatureNode()!.Visuals.IsVisibleInTree(),
                    "Sawatari must reveal exactly one visible Ninja Slayer entrance before combat.");
                _checkpoints.Write("release114.entrance." + speed);
                await NGame.Instance.ReturnToMainMenuAfterRun();
                await WaitFrames(30);
            }
        }
        finally { probe.UnpatchAll(probe.Id); }
        _checkpoints.Write("release114.completed");
        NGame.Instance!.Quit();
    }
}

internal static class Release114EntranceProbe
{
    internal static int Count;
    public static void Prefix(Player player)
    {
        var transition = NGame.Instance!.Transition;
        if (transition.InTransition || transition.GetNode<Control>("SimpleTransition").Modulate.A > 0.01f)
            throw new InvalidOperationException("Sawatari entrance started under the native black transition.");
        if (player.Creature.GetCreatureNode()!.Visuals.Visible)
            throw new InvalidOperationException("Sawatari event exposed Ninja Slayer before its entrance.");
        Count++;
    }
}
