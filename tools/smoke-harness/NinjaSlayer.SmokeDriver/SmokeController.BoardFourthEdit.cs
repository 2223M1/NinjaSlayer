using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.TestSupport;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private async Task RunFourthBoardEditsAsync()
    {
        var probe = new Harmony("NinjaSlayer.SmokeDriver.FourthBoardScry");
        probe.Patch(AccessTools.Method(typeof(ScryCmd), nameof(ScryCmd.Execute)),
            prefix: new HarmonyMethod(typeof(FourthBoardScryProbe), nameof(FourthBoardScryProbe.Prefix)));
        try
        {
            foreach (FastModeType speed in new[] { FastModeType.Normal, FastModeType.Fast })
            {
                SaveManager.Instance.PrefsSave.FastMode = speed;
                var run = await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<NinjaSlayerCharacter>(), true,
                    ActModel.GetDefaultList(), [], _configuration.Seed, GameMode.Standard, 0);
                await RunManager.Instance.EnterAct(0);
                await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<GremlinMercNormal>().ToMutable());
                var player = run.Players[0];
                await WaitUntilAsync(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play, "Fourth-board fixture did not start.");
                var combat = player.Creature.CombatState!;
                var selected = combat.CreateCreature(ModelDb.Monster<ThievingHopper>().ToMutable(), CombatSide.Enemy, null);
                await CreatureCmd.Add(selected);
                var choice = new BlockingPlayerChoiceContext();
                foreach (var enemy in combat.HittableEnemies) { enemy.SetMaxHpInternal(10000); enemy.SetCurrentHpInternal(10000); }
                async Task Play(CardModel card, Creature? target = null)
                {
                    await CardPileCmd.Add(card, PileType.Hand);
                    var action = new PlayCardAction(card, target);
                    RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(action);
                    await action.CompletionTask.WaitAsync(TimeSpan.FromSeconds(30));
                    if (action.Exception != null) throw action.Exception;
                }
                foreach (bool upgraded in new[] { false, true })
                foreach (CardModel canonical in new CardModel[] { ModelDb.Card<NinjaTaunt>(), ModelDb.Card<KunaiThrow>(), ModelDb.Card<Assess>() })
                {
                    foreach (var old in player.Piles.Where(p => p.Type != PileType.Deck).SelectMany(p => p.Cards).ToArray()) await CardPileCmd.RemoveFromCombat(old);
                    foreach (var power in player.Creature.Powers.ToArray()) await PowerCmd.Remove(power);
                    foreach (var enemy in combat.HittableEnemies)
                        foreach (var power in enemy.Powers.ToArray()) await PowerCmd.Remove(power);
                    await RemoveSmokeBlock(player.Creature);
                    await PowerCmd.Apply<DexterityPower>(choice, player.Creature, 2, player.Creature, null);
                    var card = combat.CreateCard(canonical, player);
                    if (upgraded) CardCmd.Upgrade(card);
                    player.PlayerCombatState!.GainEnergy(10);
                    for (int i = 0; i < 7; i++) await CardPileCmd.Add(combat.CreateCard<DefendIronclad>(player), PileType.Draw);
                    FourthBoardScryProbe.Player = player;
                    FourthBoardScryProbe.Amounts.Clear();
                    if (card is NinjaTaunt)
                    {
                        await PowerCmd.Apply<KaratePower>(choice, selected, 3, selected, null);
                        await Play(card, selected);
                        Require(player.Creature.Block == (upgraded ? 19 : 16) && selected.GetPowerAmount<KaratePower>() == 7
                            && !player.Creature.HasPower<KaratePower>() && combat.HittableEnemies.Where(e => e != selected).All(e => !e.HasPower<KaratePower>()),
                            "Rendered Taunt did not apply Karate only to the selected enemy while blocking its owner.");
                    }
                    else
                    {
                        var selector = new TestCardSelector();
                        selector.PrepareToSelect(Array.Empty<int>());
                        if (card is KunaiThrow)
                        {
                            await CardPileCmd.Add(combat.CreateCard<DefendIronclad>(player), PileType.Hand);
                            await CardPileCmd.Add(combat.CreateCard<StrikeIronclad>(player), PileType.Hand);
                            selector.PrepareToSelect([0]);
                            int hp = selected.CurrentHp;
                            using (CardSelectCmd.UseSelector(selector)) await Play(card, selected);
                            Require(hp - selected.CurrentHp == (upgraded ? 11 : 9) && PileType.Hand.GetPile(player).Cards.Count == 1,
                                "Rendered Kunai changed damage or failed its final one-card hand discard.");
                        }
                        else
                        {
                            using (CardSelectCmd.UseSelector(selector)) await Play(card);
                            Require(player.Creature.Block == (upgraded ? 8 : 6), "Rendered Assess did not grant native Dexterity-modified Block4/6.");
                        }
                        int expected = card is KunaiThrow ? upgraded ? 4 : 3 : upgraded ? 6 : 4;
                        Require(FourthBoardScryProbe.Amounts.SequenceEqual([expected]), "Rendered Scry amount or command count differs from the edited rule.");
                    }
                    _checkpoints.Write("fourth-board.card", data: new JsonObject { ["card"] = card.Id.ToString(), ["upgraded"] = upgraded, ["speed"] = speed.ToString() });
                }
                await NGame.Instance.ReturnToMainMenuAfterRun();
                await WaitFrames(30);
            }
        }
        finally { FourthBoardScryProbe.Player = null; probe.UnpatchAll(probe.Id); }
        _checkpoints.Write("fourth-board.completed");
    }
}

internal static class FourthBoardScryProbe
{
    internal static Player? Player;
    internal static readonly List<int> Amounts = [];
    public static void Prefix(Player __1, int __2) { if (__1 == Player) Amounts.Add(__2); }
}
