using System.Text.Json.Nodes;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private async Task RunGuardCounterAsync()
    {
        SaveManager.Instance.SetFtuesEnabled(false);
        var run = await StartSmokeRun(ModelDb.Character<NinjaSlayerCharacter>(),
            true, ActModel.GetDefaultList(), [], _configuration.Seed, GameMode.Standard, 0);
        await RunManager.Instance.EnterAct(0);
        await RunManager.Instance.EnterMapCoord(run.Map.GetAllMapPoints().First(p => p.PointType == MapPointType.Monster).coord);
        var player = LocalContext.GetMe(run)!;
        await WaitUntilAsync(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play, "Guard fixture did not start.");
        var combat = player.Creature.CombatState!;
        var targets = combat.HittableEnemies.ToArray();
        var choice = new BlockingPlayerChoiceContext();
        foreach (bool upgraded in new[] { false, true })
        foreach (var speed in new[] { FastModeType.Normal, FastModeType.Fast, FastModeType.Instant })
        {
            SaveManager.Instance.PrefsSave.FastMode = speed;
            foreach (var creature in targets.Append(player.Creature))
            {
                foreach (var power in creature.Powers.ToArray()) await PowerCmd.Remove(power);
                creature.LoseBlockInternal(creature.Block);
                creature.SetMaxHpInternal(1000);
                creature.SetCurrentHpInternal(1000);
            }
            foreach (var old in player.Piles.Where(p => p.Type != PileType.Deck).SelectMany(p => p.Cards).ToArray())
                await CardPileCmd.RemoveFromCombat(old);
            foreach (var target in targets) SetIntent(target, true);
            var card = combat.CreateCard<KillingIntent>(player);
            if (upgraded) card.UpgradeInternal();
            await CardPileCmd.Add(card, PileType.Hand);
            await PlayerCmd.SetEnergy(10, player);
            var action = new PlayCardAction(card, null);
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(action);
            await action.CompletionTask.WaitAsync(TimeSpan.FromSeconds(20));
            if (action.Exception != null) throw action.Exception;
            Require(player.Creature.Block == (upgraded ? 12 : 10), "Killing Intent must grant 10/12 Block.");
            await Hook.AfterSideTurnEnd(combat, CombatSide.Player, [player.Creature]);
            await CreatureCmd.Damage(choice, player.Creature, 2, ValueProp.Move, targets[0]);
            foreach (var target in targets) SetIntent(target, false);
            await Hook.AfterSideTurnEnd(combat, CombatSide.Enemy, targets);
            Require(targets.All(target => target.CurrentHp == 1000) && player.Creature.HasPower<KillingIntentPower>()
                && !player.PlayerCombatState!.AllCards.OfType<StraightKi>().Any(),
                "Killing Intent must wait for its owner's next turn, without retaliating.");
            await Hook.AfterPlayerTurnStart(combat, choice, player);
            var reward = PileType.Hand.GetPile(player).Cards.OfType<StraightKi>().Single();
            Require(reward.IsUpgraded == upgraded && !player.Creature.HasPower<KillingIntentPower>(),
                "Killing Intent must grant Straight Ki or Straight Ki+ and remove the pending power.");
            await Hook.AfterPlayerTurnStart(combat, choice, player);
            Require(player.PlayerCombatState!.AllCards.OfType<StraightKi>().Count() == 1,
                "Killing Intent must not grant a second reward.");
            _checkpoints.Write("guard-counter.resolved", data: new JsonObject {
                ["speed"] = speed.ToString(), ["upgraded"] = upgraded,
                ["block"] = upgraded ? 12 : 10, ["rewardUpgraded"] = reward.IsUpgraded });
        }
        _checkpoints.Write("guard-counter.completed");
        NGame.Instance!.Quit();
    }

    private static void SetIntent(Creature enemy, bool attack) => enemy.Monster!.SetMoveImmediate(
        new MoveState("guard-contract", _ => Task.CompletedTask, attack ? new SingleAttackIntent(2) : new BuffIntent()), true);
}
