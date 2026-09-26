using Godot;
using MegaCrit.Sts2.Core.AutoSlay.Helpers;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Powers;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private async Task VerifyBalanceV0216Live(string directory, CancellationToken ct)
    {
        using var manualSelection = CardSelectCmd.SuspendSelectorForTest();
        var player = LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState())!;
        var state = CombatManager.Instance.DebugOnlyGetState()!;
        var choice = new BlockingPlayerChoiceContext();
        foreach (var relic in player.Relics.ToArray()) await RelicCmd.Remove(relic);
        await CreatureCmd.GainBlock(player.Creature, 999, MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered, null);
        await PowerCmd.Apply<ThornsPower>(choice, player.Creature, 2, player.Creature, null);
        var caltrops = state.CreateCard<PlaceholderBlueDefense01>(player);
        await CardPileCmd.Add(caltrops, PileType.Hand);
        await CardCmd.AutoPlay(choice, caltrops, null);
        var training = state.CreateCard<KarateTrainingRedesignV1>(player);
        await CardPileCmd.Add(training, PileType.Hand);
        await CardCmd.AutoPlay(choice, training, null);
        for (int turn = 1; turn <= 3; turn++)
        {
            foreach (var card in player.Piles.Where(p => p.IsCombatPile).SelectMany(p => p.Cards).ToArray())
                await CardPileCmd.RemoveFromCombat(card);
            var cards = new List<CardModel>();
            for (int i = 0; i < 10; i++)
            {
                var card = state.CreateCard<DefendIronclad>(player);
                cards.Add(card);
                await CardPileCmd.Add(card, PileType.Draw);
            }
            int previousTurn = player.PlayerCombatState!.TurnNumber;
            PlayerCmd.EndTurn(player, canBackOut: false, actionDuringEnemyTurn: () => Task.CompletedTask);
            var hand = NCombatRoom.Instance!.Ui.Hand;
            await WaitUntilAsync(() => hand.IsInCardSelection,
                "Training discard selection did not appear.", ct);
            await WaitFrames(30);
            Require(PileType.Hand.GetPile(player).Cards.Count == 5, "Training must follow the normal draw.");
            var selected = hand.GetCardHolder(cards[0])!;
            selected.EmitSignal(NCardHolder.SignalName.Pressed, selected);
            await UiHelper.Click(hand.GetNode<NConfirmButton>("%SelectModeConfirmButton"));
            await WaitUntilAsync(() => player.PlayerCombatState.TurnNumber > previousTurn
                && player.PlayerCombatState.Phase == PlayerTurnPhase.Play,
                "Training did not resume play.", ct);
            Require(PileType.Hand.GetPile(player).Cards.Count == 4, "Training must discard exactly one drawn card.");
            Require(player.Creature.GetPowerAmount<ThornsPower>() == (turn < 3 ? 5 : 2),
                "Caltrops must expire after the third enemy turn without removing permanent Thorns.");
            await WaitFrames(30);
            SaveScreenshot(Path.Combine(directory, $"balance-turn-{turn}.png"));
            _checkpoints.Write($"balance-v0216.turn-{turn}");
        }
        foreach (var power in player.Creature.Powers.OfType<KarateTrainingPower>().ToArray()) await PowerCmd.Remove(power);
        var target = state.HittableEnemies.First();
        target.SetMaxHpInternal(1000);
        target.SetCurrentHpInternal(1000);
        foreach (bool upgraded in new[] { false, true })
        {
            var token = state.CreateCard<StrongShurikenTokenRedesignV1>(player);
            if (upgraded) CardCmd.Upgrade(token);
            await CardPileCmd.Add(token, PileType.Hand);
            await CardCmd.AutoPlay(choice, token, target);
            Require(token.Pile?.Type == PileType.Exhaust, "Strong Shuriken must finish in exhaust.");
        }
        _checkpoints.Write("balance-v0216.strong-shuriken");
        foreach (var card in PileType.Hand.GetPile(player).Cards.ToArray()) await CardPileCmd.RemoveFromCombat(card);
        foreach (Type type in new[] { typeof(KarateTrainingRedesignV1), typeof(PlaceholderBlueDefense01),
            typeof(LuckyStrikeRedesignV1), typeof(SpiralRoundhouseJumpRedesignV1), typeof(GuidingFlameRedesignV1),
            typeof(TechniqueSearchRedesignV1), typeof(KarateTeaRedesignV1), typeof(HellTornadoRedesignV1),
            typeof(DragonFlyingKickRedesignV1), typeof(NarakuFormRedesignV1) })
        {
            var card = state.CreateCard(ModelDb.GetById<CardModel>(ModelDb.GetId(type)), player);
            await CardPileCmd.Add(card, PileType.Hand);
        }
        await WaitFrames(90);
        SaveScreenshot(Path.Combine(directory, "balance-card-art.png"));
        _checkpoints.Write("balance-v0216.completed");
    }
}
