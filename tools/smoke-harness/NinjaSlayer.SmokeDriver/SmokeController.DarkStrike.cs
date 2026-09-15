using Godot;
using MegaCrit.Sts2.Core.AutoSlay.Helpers;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Events;
using NinjaSlayer.Monsters;
using NinjaSlayer.Powers;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private async Task VerifyDarkStrikeEventRewards(string directory, Action<string> section, CancellationToken ct)
    {
        SaveManager.Instance.MarkFtueAsComplete("combat_reward_ftue");
        SaveManager.Instance.MarkFtueAsComplete("obtain_relic_ftue");
        foreach (bool thorns in new[] { false, true })
        {
            string scenario = thorns ? "thorns-all-four" : "normal-take-one-skip-three";
            section($"dark-event-{scenario}");
            var eventRoom = (EventRoom)await RunManager.Instance.EnterRoomDebug(
                RoomType.Event, model: ModelDb.Event<DarkNinjaEvent>());
            await WaitUntilAsync(() => Options().Length == 2, "Dark Ninja initial event options missing", ct);
            await UiHelper.Click(Options()[1]);
            await WaitUntilAsync(() => Options().Length == 1, "Dark Ninja fight option missing", ct);
            await UiHelper.Click(Options()[0]);
            await WaitUntilAsync(() => CombatManager.Instance.IsInProgress
                && LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState())?.PlayerCombatState?.Phase == PlayerTurnPhase.Play,
                "Dark Ninja event combat did not start", ct);

            var combat = CombatManager.Instance.DebugOnlyGetState()!;
            var player = LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState())!;
            var dark = (DarkNinjaMonster)combat.Enemies.Single(c => c.Monster is DarkNinjaMonster).Monster!;
            var choice = new BlockingPlayerChoiceContext();
            await PowerCmd.Remove<EvasionPower>(dark.Creature);
            await PowerCmd.Remove<EvasionPower>(player.Creature);
            await PowerCmd.Remove<BufferPower>(player.Creature);
            await PowerCmd.Remove<NarakuLifePower>(player.Creature);
            if (player.Creature.Block > 0) await RemoveSmokeBlock(player.Creature);
            player.Creature.SetMaxHpInternal(500);
            await CreatureCmd.SetCurrentHp(player.Creature, 500);
            foreach (CardModel card in CardPile.GetCards(player, PileType.Draw, PileType.Discard).ToArray())
                await CardPileCmd.RemoveFromCombat(card);
            var originals = new List<CardModel>();
            for (int index = 0; index < 4; index++)
            {
                CardModel deck = player.RunState.CreateCard(ModelDb.Card<PlaceholderBlueDefense01>(), player);
                if (index % 2 == 0) CardCmd.Upgrade(deck);
                await CardPileCmd.Add(deck, PileType.Deck);
                originals.Add(deck);
                CardModel copy = combat.CloneCard(deck);
                copy.DeckVersion = deck;
                await CardPileCmd.Add(copy, PileType.Draw);
            }
            var strike = (MoveState)dark.MoveStateMachine!.States[DarkNinjaMonster.DarkStrikeMoveId];
            for (int index = 0; index < 4; index++)
            {
                if (thorns && index == 3)
                {
                    await PowerCmd.Apply<ThornsPower>(choice, player.Creature, 999, player.Creature, null);
                    await CreatureCmd.SetCurrentHp(dark.Creature, 1);
                }
                await strike.PerformMove([player.Creature]);
            }
            if (!thorns) await CreatureCmd.Kill(dark.Creature, force: true);
            await CombatManager.Instance.CheckWinCondition();
            await WaitUntilAsync(() => NOverlayStack.Instance?.Peek() is NRewardsScreen,
                "Event combat did not show native card-return rewards", ct);
            var cardScreen = (NRewardsScreen)NOverlayStack.Instance!.Peek()!;
            var returns = UiHelper.FindAll<NRewardButton>(cardScreen)
                .Where(button => button.Reward is SpecialCardReward).ToArray();
            Require(returns.Length == 4 && originals.All(card => !player.Deck.Cards.Contains(card)),
                "Event reward UI must offer all four stolen cards before any are reclaimed.");
            Require(player.RunState.CurrentRoom is CombatRoom && !eventRoom.LocalMutableEvent.IsFinished,
                "Event victory ran before the stolen-card rewards.");
            await WaitFrames(45);
            _tree.Root.GetTexture().GetImage().SavePng(Path.Combine(directory, $"event-{scenario}-cards.png"));
            foreach (NRewardButton button in thorns ? returns : returns.Take(1))
            {
                await UiHelper.Click(button);
                await WaitFrames(15);
            }
            Require(originals.Count(card => player.Deck.Cards.Contains(card)) == (thorns ? 4 : 1),
                "Native reward buttons did not restore the selected permanent cards.");
            await UiHelper.Click(UiHelper.FindFirst<NProceedButton>(cardScreen)!);
            await WaitUntilAsync(() => NOverlayStack.Instance?.Peek() is NRewardsScreen next
                && next != cardScreen && UiHelper.FindAll<NRewardButton>(next).Count(button => button.Reward is RelicReward) == 2,
                "Event victory did not follow stolen cards with the two existing relic rewards", ct);
            var relicScreen = (NRewardsScreen)NOverlayStack.Instance!.Peek()!;
            Require(UiHelper.FindAll<NRewardButton>(relicScreen).All(button => button.Reward is not SpecialCardReward),
                "Stolen-card rewards were offered a second time by event victory.");
            await WaitFrames(30);
            _tree.Root.GetTexture().GetImage().SavePng(Path.Combine(directory, $"event-{scenario}-relics.png"));
            await UiHelper.Click(UiHelper.FindFirst<NProceedButton>(relicScreen)!);
            await WaitUntilAsync(() => NOverlayStack.Instance?.Peek() is not NRewardsScreen,
                "Event relic rewards did not close", ct);
            Require(originals.Count(card => player.Deck.Cards.Contains(card)) == (thorns ? 4 : 1),
                "Skipping the remaining rewards changed which stolen cards returned.");
            _checkpoints.Write($"dark-strike.event-{scenario}-passed");
        }

        static NEventOptionButton[] Options() => NEventRoom.Instance == null ? []
            : UiHelper.FindAll<NEventOptionButton>(NEventRoom.Instance)
                .Where(button => button.IsEnabled && button.IsVisibleInTree()).ToArray();
    }
}
