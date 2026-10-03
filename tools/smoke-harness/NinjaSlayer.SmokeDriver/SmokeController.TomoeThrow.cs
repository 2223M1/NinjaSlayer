using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Content;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private async Task RunTomoeThrowAsync()
    {
        SaveManager.Instance.SetFtuesEnabled(false);
        STS2RitsuLib.Data.ModDataStore.For("NinjaSlayer")
            .Get<NinjaSlayerSettingsData>("ninja_slayer_settings").BriefBossGreetingEnabled = true;
        foreach (int playerCount in new[] { 2, 4, 1 })
        {
            // Native player models and action queue in one rendered client.
            // This tests multiplayer layout/scaling, not network transport.
            var player = Player.CreateForNewRun<NinjaSlayerCharacter>(UnlockState.all, 1);
            var players = new List<Player> { player };
            for (int i = 1; i < playerCount; i++)
                players.Add(Player.CreateForNewRun<Ironclad>(UnlockState.all, (ulong)i + 1));
            var run = RunState.CreateForNewRun(players, ActModel.GetDefaultList().Select(a => a.ToMutable()).ToList(),
                [], GameMode.Standard, 0, _configuration.Seed);
            RunManager.Instance.SetUpTest(run, new NetSingleplayerGameService(), true, false);
            AccessTools.Method(typeof(RunManager), "GenerateRooms").Invoke(RunManager.Instance, null);
            await (Task)AccessTools.Method(typeof(NGame), "StartRun").Invoke(NGame.Instance, [run])!;
            await RunManager.Instance.EnterAct(1);
            await RunManager.Instance.EnterRoomDebug(RoomType.Boss, model: ModelDb.Encounter<KaiserCrabBoss>().ToMutable());
            await WaitUntilAsync(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play, "Tomoe crab fixture did not start.");
            await WaitFrames(30);
            var combat = player.Creature.CombatState!;
            var node = NCombatRoom.Instance!.GetCreatureNode(player.Creature)!;
            var root = node.GetGlobalTransform();
            var targets = combat.Enemies.ToList();
            Require(targets.Count == 2 && targets.Any(c => c.Monster is Crusher) && targets.Any(c => c.Monster is Rocket),
                "Tomoe fixture must use the real Emperor Crab encounter.");
            var ordinary = combat.CreateCreature(ModelDb.Monster<ThievingHopper>().ToMutable(), CombatSide.Enemy, null);
            await CreatureCmd.Add(ordinary);
            targets.Add(ordinary);
            foreach (bool upgraded in new[] { false, true })
            foreach (var target in targets)
            {
                SaveManager.Instance.PrefsSave.FastMode = upgraded ? FastModeType.Fast : FastModeType.Normal;
                foreach (var held in player.PlayerCombatState!.Hand.Cards.ToArray())
                    await CardPileCmd.RemoveFromCombat(held);
                var tea = combat.CreateCard<Chado>(player);
                var card = combat.CreateCard<TomoeThrow>(player);
                if (upgraded) card.UpgradeInternal();
                await CardPileCmd.Add(tea, PileType.Hand);
                await CardPileCmd.Add(card, PileType.Hand);
                var victim = NCombatRoom.Instance.GetCreatureNode(target)!;
                var body = victim.Visuals.GetCurrentBody();
                var bodyBefore = body.Transform;
                var centerBefore = victim.Visuals.VfxSpawnPosition.Transform;
                decimal beforeBlock = player.Creature.Block;
                int beforeWeak = target.GetPowerAmount<WeakPower>();
                _checkpoints.Write("tomoe.start", data: new JsonObject
                { ["players"] = playerCount, ["target"] = target.Monster!.Id.ToString(), ["upgraded"] = upgraded,
                    ["bodyVisible"] = body.Visible, ["hasTexture"] = body is not Sprite2D s || s.Texture != null });
                var selector = new TestCardSelector();
                selector.PrepareToSelect([0]);
                using (CardSelectCmd.UseSelector(selector))
                {
                    var action = new PlayCardAction(card, target);
                    RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(action);
                    await action.CompletionTask.WaitAsync(TimeSpan.FromSeconds(10));
                    if (action.Exception != null) throw action.Exception;
                }
                await WaitFrames(30);
                Require(tea.Pile?.Type == PileType.Exhaust && card.Pile?.Type == PileType.Discard,
                    "Tomoe did not finish tea exhaustion and leave the play pile.");
                Require(player.Creature.Block - beforeBlock == (upgraded ? 7 : 6)
                    && target.GetPowerAmount<WeakPower>() - beforeWeak == (upgraded ? 3 : 2),
                    "Tomoe must apply its block and selected-target Weak exactly once.");
                Require(node.GetGlobalTransform().IsEqualApprox(root) && body.Transform.IsEqualApprox(bodyBefore)
                    && victim.Visuals.VfxSpawnPosition.Transform.IsEqualApprox(centerBefore),
                    "Tomoe left a creature or targeting marker displaced.");
                Require(node.Visuals.FindChild("TomoeThrow", true, false) == null
                    && victim.Visuals.FindChild("GrappleExposure", true, false) == null,
                    "Tomoe left animation or exposure nodes behind.");
                var followup = combat.CreateCard<DefendNinjaSlayer>(player);
                player.PlayerCombatState.GainEnergy(1);
                await CardPileCmd.Add(followup, PileType.Hand);
                var followupAction = new PlayCardAction(followup, null);
                RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(followupAction);
                await followupAction.CompletionTask.WaitAsync(TimeSpan.FromSeconds(10));
                if (followupAction.Exception != null) throw followupAction.Exception;
                Require(followup.Pile?.Type == PileType.Discard, "Follow-up card did not complete after Tomoe.");
                _checkpoints.Write("tomoe.resolved", data: new JsonObject
                { ["players"] = playerCount, ["target"] = target.Monster!.Id.ToString(), ["upgraded"] = upgraded });
            }
            await NGame.Instance!.ReturnToMainMenuAfterRun();
            await WaitFrames(30);
        }
        _checkpoints.Write("tomoe.completed");
        NGame.Instance!.Quit();
    }
}
