using Godot;
using MegaCrit.Sts2.Core.AutoSlay.Helpers;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Orbs;
using NinjaSlayer.Relics;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private async Task VerifyMotherUnixLive(string directory, CancellationToken ct)
    {
        using var manualSelection = CardSelectCmd.SuspendSelectorForTest();
        var player = LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState())!;
        var state = CombatManager.Instance.DebugOnlyGetState()!;
        foreach (var existing in player.Relics.ToArray()) await RelicCmd.Remove(existing);
        var relic = await RelicCmd.Obtain<MotherUnixRelic>(player);
        var holder = UiHelper.FindAll<NRelicInventoryHolder>(_tree.Root).Single(node => node.Relic.Model == relic);
        holder.EmitSignal(Control.SignalName.MouseEntered);
        await WaitFrames(60);
        SaveScreenshot(Path.Combine(directory, "mother-unix-tooltip.png"));
        holder.EmitSignal(Control.SignalName.MouseExited);

        foreach (string scenario in new[] { "keep", "discard", "nested" })
        {
            foreach (var card in player.Piles.Where(pile => pile.IsCombatPile).SelectMany(pile => pile.Cards).ToArray())
                await CardPileCmd.RemoveFromCombat(card);
            var cards = new List<CardModel>();
            for (int i = 0; i < 10; i++)
            {
                CardModel card = scenario == "nested" && i == 0
                    ? state.CreateCard<ShurikenCreation>(player) : state.CreateCard<DefendIronclad>(player);
                cards.Add(card);
                await CardPileCmd.Add(card, PileType.Draw);
            }
            int previousTurn = player.PlayerCombatState!.TurnNumber;
            PlayerCmd.EndTurn(player, canBackOut: false, actionDuringEnemyTurn: () => Task.CompletedTask);
            await CompleteSelection(cards.Take(3).ToArray(), scenario == "keep" ? [] : cards.Take(2).ToArray(), scenario);
            if (scenario == "nested")
                await CompleteSelection(cards.Skip(2).Take(1).ToArray(), [cards[2]], "nested-sly");
            await WaitUntilAsync(() => player.PlayerCombatState!.TurnNumber > previousTurn
                    && player.PlayerCombatState.Phase == PlayerTurnPhase.Play,
                "Mother UNIX did not resume the native player turn.", ct);
            int removed = scenario == "nested" ? 3 : scenario == "discard" ? 2 : 0;
            Require(PileType.Hand.GetPile(player).Cards.SequenceEqual(cards.Skip(removed).Take(5)),
                "Normal turn draw did not use the post-Scry draw pile.");
            if (scenario == "nested")
                Require(player.PlayerCombatState.OrbQueue.Orbs.OfType<ShurikenOrb>().Single().StackCount == 2,
                    "Nested Sly must complete before native hand draw resumes.");
            await WaitFrames(30);
            SaveScreenshot(Path.Combine(directory, scenario + "-after-draw.png"));
            _checkpoints.Write("mother-unix." + scenario + "-before-draw");
        }
        _checkpoints.Write("mother-unix.completed");

        async Task CompleteSelection(CardModel[] expected, CardModel[] selected, string label)
        {
            await WaitUntilAsync(() => UiHelper.FindFirst<NSimpleCardSelectScreen>(_tree.Root) != null,
                "Mother UNIX selection did not appear.", ct);
            var screen = UiHelper.FindFirst<NSimpleCardSelectScreen>(_tree.Root)!;
            await WaitFrames(30);
            var grid = screen.GetNode<NCardGrid>("%CardGrid");
            Require(grid.CurrentlyDisplayedCardHolders.Select(item => item.CardModel).ToHashSet().SetEquals(expected),
                "Native selection did not show the expected undrawn cards.");
            Require(PileType.Hand.GetPile(player).IsEmpty, "Normal hand draw happened before Scry finished.");
            SaveScreenshot(Path.Combine(directory, label + "-selection.png"));
            foreach (var card in selected)
            {
                var cardHolder = grid.CurrentlyDisplayedCardHolders.Single(item => item.CardModel == card);
                cardHolder.EmitSignal(NCardHolder.SignalName.Pressed, cardHolder);
            }
            await UiHelper.Click(screen.GetNode<NConfirmButton>("%Confirm"));
            await WaitUntilAsync(() => !GodotObject.IsInstanceValid(screen) || !screen.IsInsideTree(),
                "Native selection did not close.", ct);
        }
    }
}
