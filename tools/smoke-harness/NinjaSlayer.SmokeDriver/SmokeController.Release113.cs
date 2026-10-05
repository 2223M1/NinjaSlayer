using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Content;
using NinjaSlayer.Monsters;
using NinjaSlayer.Powers;
using NinjaSlayer.Relics;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private async Task RunRelease113Async()
    {
        SaveManager.Instance.SetFtuesEnabled(false);
        foreach (FastModeType speed in new[] { FastModeType.Normal, FastModeType.Fast })
        {
            SaveManager.Instance.PrefsSave.FastMode = speed;
            var player = Player.CreateForNewRun<NinjaSlayerCharacter>(UnlockState.all, 1);
            var run = RunState.CreateForNewRun([player], ActModel.GetDefaultList().Select(a => a.ToMutable()).ToList(),
                [], GameMode.Standard, 0, _configuration.Seed);
            RunManager.Instance.SetUpTest(run, new NetSingleplayerGameService(), true, false);
            AccessTools.Method(typeof(RunManager), "GenerateRooms").Invoke(RunManager.Instance, null);
            await (Task)AccessTools.Method(typeof(NGame), "StartRun").Invoke(NGame.Instance, [run])!;
            await RelicCmd.Obtain(ModelDb.Relic<BagOfPreparation>().ToMutable(), player);
            await RelicCmd.Obtain(ModelDb.Relic<OrigamiPactRelic>().ToMutable(), player);
            await RunManager.Instance.EnterAct(1);
            await RunManager.Instance.EnterRoomDebug(RoomType.Elite, model: ModelDb.Encounter<EntomancerElite>().ToMutable());
            await WaitUntilAsync(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play, "Hive fixture did not start.");
            var combat = player.Creature.CombatState!;
            var hive = combat.Enemies.Single();
            hive.SetMaxHpInternal(10000);
            hive.SetCurrentHpInternal(10000);
            Require(hive.HasPower<PersonalHivePower>(), "Fixture must use the native Entomancer Personal Hive.");
            var choice = new BlockingPlayerChoiceContext();
            async Task ClearPiles()
            {
                foreach (var card in player.Piles.Where(p => p.Type != PileType.Deck).SelectMany(p => p.Cards).ToArray())
                    await CardPileCmd.RemoveFromCombat(card);
            }
            CardModel Card<T>(bool upgraded = false) where T : CardModel
            {
                var card = combat.CreateCard<T>(player);
                if (upgraded) CardCmd.Upgrade(card);
                return card;
            }
            async Task Play(CardModel card, MegaCrit.Sts2.Core.Entities.Creatures.Creature? target = null)
            {
                var action = new PlayCardAction(card, target);
                RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(action);
                await action.CompletionTask.WaitAsync(TimeSpan.FromSeconds(30));
                if (action.Exception != null) throw action.Exception;
            }
            await ClearPiles();
            var tea = (Chado)Card<Chado>();
            tea.IncreaseEnergy(1);
            var storm = Card<ShurikenStorm>();
            var prep = Card<BladePrep>();
#if NINJASLAYER_CHANNEL_STABLE
            var abundance = Card<DefendNinjaSlayer>(); // Abundance is preview-only; this slot's reported result is injected below.
#else
            var abundance = Card<Abundance>();
#endif
            foreach (var card in new[] { tea, storm, Card<ReadTheEnemy>(), Card<Clumsy>(), prep,
                         Card<StrikeStrike>(true), Card<ShurikenCreation>(true), abundance })
                await CardPileCmd.Add(card, PileType.Hand);
            var guard = Card<CrossGuard>();
            await CardPileCmd.Add(guard, PileType.Draw);
            for (int i = 0; i < 15; i++) await CardPileCmd.Add(Card<DefendNinjaSlayer>(), PileType.Draw);
            Require(PileType.Hand.GetPile(player).Cards.Count == 8 && PileType.Draw.GetPile(player).Cards.Count == 16
                && PileType.Discard.GetPile(player).Cards.Count == 0, "Reported opening pile counts were not reconstructed.");
            player.PlayerCombatState!.LoseEnergy(player.PlayerCombatState.Energy);
            player.PlayerCombatState.GainEnergy(3);
            // Inject the player's reported Abundance outcome, not its preceding run RNG history.
            await CardPileCmd.Add(abundance, PileType.Exhaust);
            player.PlayerCombatState.LoseEnergy(1);
            var planning = Card<Forethought>(true);
            planning.SetToFreeThisTurn();
            await CardPileCmd.AddGeneratedCardToCombat(planning, PileType.Hand, player);
            await Play(planning);
            await Play(prep);
            await Play(tea);
            await Play(guard);
            var selector = new TestCardSelector();
            selector.PrepareToSelect(Array.Empty<int>());
            using (CardSelectCmd.UseSelector(selector)) await Play(storm);
            var koki = player.PlayerCombatState.Pets.Single(p => p.Monster is YamotoKokiMonster);
            var monster = (YamotoKokiMonster)koki.Monster!;
            monster.SetMoveImmediate((MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine.MoveState)
                monster.MoveStateMachine!.States[YamotoKokiMonster.IaiSlashMoveId], forceTransition: true);
            await PowerCmd.Apply<IntangiblePower>(choice, player.Creature, 3, player.Creature, null);
            PlayerCmd.EndTurn(player, true);
            await WaitUntilAsync(() => player.PlayerCombatState.TurnNumber >= 2
                && player.PlayerCombatState.Phase == PlayerTurnPhase.Play, "Yamoto Hive turn start blocked the native turn loop.");
            Require(!player.PlayerCombatState.AllCards.OfType<Dazed>().Any(), "Yamoto must not give its owner Hive statuses.");
            var followup = Card<DefendNinjaSlayer>();
            await CardPileCmd.Add(followup, PileType.Hand);
            await Play(followup);
            PlayerCmd.EndTurn(player, true);
            await WaitUntilAsync(() => player.PlayerCombatState.TurnNumber >= 3
                && player.PlayerCombatState.Phase == PlayerTurnPhase.Play, "The turn following the Hive fix could not finish.");
            _checkpoints.Write("release113.hive." + speed, data: new JsonObject
                { ["reportedAbundanceOutcomeInjected"] = true, ["turn"] = player.PlayerCombatState.TurnNumber });

            foreach (bool upgraded in new[] { false, true })
            {
                await ClearPiles();
                foreach (var power in player.Creature.Powers.ToArray()) await PowerCmd.Remove(power);
                for (int i = 0; i < 7; i++) await CardPileCmd.Add(Card<DefendNinjaSlayer>(), PileType.Draw);
                var adapt = Card<Adapt>(upgraded);
                await CardPileCmd.Add(adapt, PileType.Hand);
                player.PlayerCombatState.GainEnergy(10);
                await Play(adapt);
                Require(PileType.Hand.GetPile(player).Cards.Count == (upgraded ? 4 : 3), "Rendered Adapt draw count differs.");
                var next = PileType.Hand.GetPile(player).Cards[0];
                await Play(next);
                Require(PileType.Draw.GetPile(player).Cards[0] == next && !player.Creature.HasPower<ReboundPower>(),
                    "Rendered Adapt did not rebound exactly the next card.");
                var resilience = Card<Resilience>(upgraded);
                await CardPileCmd.Add(resilience, PileType.Hand);
                await Play(resilience);
                int before = PileType.Hand.GetPile(player).Cards.Count;
                await CardPileCmd.AddGeneratedCardToCombat(Card<Wound>(), PileType.Discard, player);
                Require(PileType.Hand.GetPile(player).Cards.Count == before + (upgraded ? 2 : 1), "Rendered generation draw differs.");
                _checkpoints.Write("release113.cards." + speed + "." + upgraded);
            }
            foreach (var enemy in combat.Enemies.Where(enemy => enemy.IsAlive).ToArray())
            {
                await CreatureCmd.SetCurrentHp(enemy, 1);
                await RemoveSmokeBlock(enemy);
                var finish = Card<StrikeNinjaSlayer>();
                await CardPileCmd.Add(finish, PileType.Hand);
                player.PlayerCombatState.GainEnergy(2);
                await Play(finish, enemy);
            }
            await WaitUntilAsync(() => !CombatManager.Instance.IsInProgress, "Hive fixture did not settle.");
            await NGame.Instance!.ReturnToMainMenuAfterRun();
            await WaitFrames(30);
        }
        await RunFourthBoardEditsAsync();
        _checkpoints.Write("release113.completed");
        NGame.Instance!.Quit();
    }
}
