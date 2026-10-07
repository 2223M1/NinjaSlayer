using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
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
    private async Task RunStrikeSynergyAsync()
    {
        SaveManager.Instance.SetFtuesEnabled(false);
        foreach (FastModeType speed in new[] { FastModeType.Normal, FastModeType.Fast })
        {
            SaveManager.Instance.PrefsSave.FastMode = speed;
            var player = Player.CreateForNewRun<NinjaSlayerCharacter>(UnlockState.all, 1);
            var run = RunState.CreateForNewRun([player], ActModel.GetDefaultList().Select(act => act.ToMutable()).ToList(),
                [], GameMode.Standard, 0, _configuration.Seed);
            RunManager.Instance.SetUpTest(run, new NetSingleplayerGameService(), true, false);
            AccessTools.Method(typeof(RunManager), "GenerateRooms").Invoke(RunManager.Instance, null);
            await (Task)AccessTools.Method(typeof(NGame), "StartRun").Invoke(NGame.Instance, [run])!;
            await RelicCmd.Obtain<StrikeDummy>(player);
            await RelicCmd.Obtain<FakeStrikeDummy>(player);
            await RunManager.Instance.EnterAct(0);
            await RunManager.Instance.EnterMapCoord(run.Map.GetAllMapPoints().First(point => point.PointType == MegaCrit.Sts2.Core.Map.MapPointType.Monster).coord);
            await WaitUntilAsync(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play, "Strike fixture did not start.");
            var combat = player.Creature.CombatState!;
            var enemy = combat.Enemies.First();
            foreach (var target in combat.Enemies) { target.SetMaxHpInternal(10000); target.SetCurrentHpInternal(10000); }
            foreach (var card in player.Piles.Where(pile => pile.Type != PileType.Deck).SelectMany(pile => pile.Cards).ToArray())
                await CardPileCmd.RemoveFromCombat(card);
            player.PlayerCombatState!.GainEnergy(100);
            var selector = new TestCardSelector();
            using var selection = CardSelectCmd.UseSelector(selector);
            async Task Play(CardModel card, int expectedDamage)
            {
                if (card.Pile?.Type != PileType.Hand) await CardPileCmd.Add(card, PileType.Hand);
                selector.PrepareToSelect(Array.Empty<int>());
                int before = enemy.CurrentHp;
                var action = new PlayCardAction(card, enemy);
                RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(action);
                await action.CompletionTask.WaitAsync(TimeSpan.FromSeconds(30));
                if (action.Exception != null) throw action.Exception;
                Require(before - enemy.CurrentHp == expectedDamage, $"{card.Id} rendered action did not gain its exact native Strike bonus.");
                _checkpoints.Write("strike-synergy.damage", data: new JsonObject
                { ["card"] = card.Id.ToString(), ["upgraded"] = card.IsUpgraded, ["speed"] = speed.ToString(), ["damage"] = expectedDamage });
            }
            foreach (bool upgraded in new[] { false, true })
            {
                CardModel[] strikes = [combat.CreateCard<StrikeNinjaSlayer>(player), combat.CreateCard<ChopStrike>(player), combat.CreateCard<StrikeStrike>(player)];
                foreach (var card in strikes)
                {
                    if (upgraded) CardCmd.Upgrade(card);
                    Require(card.Tags.Contains(CardTag.Strike), "Rendered strike is missing its native tag.");
                    await Play(card, (int)card.DynamicVars.Damage.BaseValue + 4);
                }
                var generated = player.PlayerCombatState.AllCards.OfType<StrikeStrike>()
                    .Single(card => card.Pile?.Type == PileType.Hand && card.IsUpgraded == upgraded);
                Require(generated.Tags.Contains(CardTag.Strike), "Generated rendered strike lost its native tag.");
                await Play(generated, (int)generated.DynamicVars.Damage.BaseValue + 4);
                var chop = combat.CreateCard<Chop>(player);
                if (upgraded) CardCmd.Upgrade(chop);
                await Play(chop, (int)chop.DynamicVars.Damage.BaseValue);
                var perfected = combat.CreateCard<PerfectedStrike>(player);
                if (upgraded) CardCmd.Upgrade(perfected);
                await CardPileCmd.Add(perfected, PileType.Hand);
                int count = player.PlayerCombatState.AllCards.Count(card => card.Tags.Contains(CardTag.Strike));
                int calculated = 6 + (upgraded ? 3 : 2) * count;
                Require(perfected.DynamicVars.CalculatedDamage.Calculate(enemy) == calculated,
                    "Rendered Perfected Strike preview did not count owned generated/discarded/exhausted Strikes.");
                await Play(perfected, calculated + 4);
            }
            await NGame.Instance!.ReturnToMainMenuAfterRun();
            await WaitFrames(20);
        }
        _checkpoints.Write("strike-synergy.completed");
        NGame.Instance!.Quit();
    }
}
