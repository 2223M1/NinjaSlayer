using Godot;
using HarmonyLib;
using System.Text.Json.Nodes;
using MegaCrit.Sts2.Core.AutoSlay;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Events.Custom;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Content;
using NinjaSlayer.Orbs;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private async Task RunRelease100Async()
    {
        SaveManager.Instance.SetFtuesEnabled(false);
        var run = await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<NinjaSlayerCharacter>(),
            true, ActModel.GetDefaultList(), [], _configuration.Seed, GameMode.Standard, 0);
        await RunManager.Instance.EnterAct(0);
        await RunManager.Instance.EnterMapCoord(run.Map.GetAllMapPoints().First(p => p.PointType == MapPointType.Monster).coord);
        var player = LocalContext.GetMe(run)!;
        await WaitUntilAsync(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play, "1.0.0 fixture did not start.");
        var combat = player.Creature.CombatState!;
        foreach (var enemy in combat.Enemies) { enemy.SetMaxHpInternal(10000); enemy.SetCurrentHpInternal(10000); }
        var choice = new BlockingPlayerChoiceContext();
        var tea = combat.CreateCard<Chado>(player);
        await CardPileCmd.Add(tea, PileType.Hand);
        await CardCmd.Exhaust(choice, tea);
        Func<bool> autoslay = NonInteractiveMode.AutoSlayerCheck;
        NonInteractiveMode.AutoSlayerCheck = static () => false;
        try
        {
            await WaitFrames(120);
            var exhaustButton = NCombatRoom.Instance!.Ui.ExhaustPile;
            await MouseClickAtAsync(exhaustButton.GetGlobalRect().GetCenter());
            await WaitFrames(3);
            Require(NCapstoneContainer.Instance?.CurrentCapstoneScreen is NCardPileScreen,
                "Fixture must open the native exhaust pile through mouse input before testing Scry.");
            NCapstoneContainer.Instance!.Close();
            await WaitFrames(30);
            foreach (var speed in new[] { FastModeType.Normal, FastModeType.Fast, FastModeType.Instant })
            foreach (int clickCount in new[] { 1, 2 })
            {
                SaveManager.Instance.PrefsSave.FastMode = speed;
                var cards = Enumerable.Range(0, 3).Select(_ => combat.CreateCard<DefendNinjaSlayer>(player)).ToArray();
                // Enter the draw pile from a visible native pile, so its animation counter
                // is initialized too. Adding new models straight to Draw omits that animation.
                await CardPileCmd.Add(cards, PileType.Hand);
                await WaitFrames(120);
                await CardPileCmd.Add(cards, PileType.Draw, CardPilePosition.Top);
                await WaitFrames(120);
                await (Task)AccessTools.Method(typeof(ShurikenOrb), "AddStock").Invoke(null, [choice, player, 3])!;
                Task<ScryResult> scry = ScryCmd.Execute(choice, player, 3);
                NSimpleCardSelectScreen? screen = null;
                await WaitUntilAsync(() => (screen = FindDescendant<NSimpleCardSelectScreen>(_tree.Root)) != null,
                    "Scry selection did not open.");
                await WaitFrames(120);
                string initialDisplay = NCombatRoom.Instance!.Ui.DrawPile.GetNode<Label>("CountContainer/Count").Text;
                int initialActual = PileType.Draw.GetPile(player).Cards.Count;
                Require(initialDisplay == initialActual.ToString(),
                    $"Fixture must start Scry with an accurate native draw-pile counter: {initialDisplay} vs {initialActual}.");
                foreach (var card in cards)
                    AccessTools.Method(typeof(NSimpleCardSelectScreen), "OnCardClicked").Invoke(screen, [card]);
                var confirm = screen!.GetNode<NButton>("%Confirm");
                var exhaust = NCombatRoom.Instance!.Ui.ExhaustPile;
                Rect2 overlap = confirm.GetGlobalRect().Intersection(exhaust.GetGlobalRect());
                Vector2 point = overlap.HasArea() ? overlap.GetCenter() : confirm.GetGlobalRect().GetCenter();
                _checkpoints.Write("release100.confirm-geometry", data: new JsonObject
                { ["speed"] = speed.ToString(), ["clicks"] = clickCount, ["confirm"] = point.ToString(),
                    ["exhaust"] = exhaust.GetGlobalRect().ToString(), ["overlap"] = exhaust.GetGlobalRect().HasPoint(point) });
                for (int click = 0; click < clickCount; click++)
                {
                    await MouseClickAtAsync(point);
                    await WaitFrames(1);
                }
                await WaitUntilAsync(() => scry.IsCompleted, "Mouse confirmation did not finish Scry.");
                await scry;
                await WaitFrames(3);
                bool opened = NCapstoneContainer.Instance?.CurrentCapstoneScreen is NCardPileScreen;
                _checkpoints.Write("release100.scry-mouse", data: new JsonObject
                { ["speed"] = speed.ToString(), ["clicks"] = clickCount, ["pileOpened"] = opened,
                    ["discarded"] = scry.Result.Discarded });
                Require(!opened, "Scry confirmation opened a combat pile.");
                Require(scry.Result.Discarded == 3, "Mouse confirmation lost selected Scry cards.");
                Require(!overlap.HasArea(), "Scry confirmation still overlaps the exhaust pile button.");
                await WaitFrames(120);
                int drawCount = PileType.Draw.GetPile(player).Cards.Count;
                string displayedDrawCount = NCombatRoom.Instance!.Ui.DrawPile
                    .GetNode<Label>("CountContainer/Count").Text;
                _checkpoints.Write("release100.scry-pile-count", data: new JsonObject
                { ["speed"] = speed.ToString(), ["clicks"] = clickCount,
                    ["actual"] = drawCount, ["displayed"] = displayedDrawCount });
                Require(displayedDrawCount == drawCount.ToString(),
                    $"Scry left draw-pile count stale: displayed {displayedDrawCount}, actual {drawCount}.");
            }
            foreach (var (selected, exhaustSelection) in new[] { (0, false), (1, false), (3, false), (3, true) })
            {
                await CardPileCmd.Add(PileType.Draw.GetPile(player).Cards.ToArray(), PileType.Discard);
                var cards = Enumerable.Range(0, 3).Select(_ => combat.CreateCard<DefendNinjaSlayer>(player)).ToArray();
                await CardPileCmd.Add(cards, PileType.Hand);
                await WaitFrames(120);
                await CardPileCmd.Add(cards, PileType.Draw, CardPilePosition.Top);
                await WaitFrames(120);
                Require(NCombatRoom.Instance!.Ui.DrawPile.GetNode<Label>("CountContainer/Count").Text == "3",
                    "Fixture must show three cards before the selection-count scenarios.");
                Task<ScryResult> scry = ScryCmd.Execute(choice, player, 3, exhaustSelection);
                NSimpleCardSelectScreen? screen = null;
                await WaitUntilAsync(() => (screen = FindDescendant<NSimpleCardSelectScreen>(_tree.Root)) != null,
                    "Scry count selection did not open.");
                await WaitFrames(120);
                foreach (var card in cards.Take(selected))
                    AccessTools.Method(typeof(NSimpleCardSelectScreen), "OnCardClicked").Invoke(screen, [card]);
                await MouseClickAtAsync(screen!.GetNode<NButton>("%Confirm").GetGlobalRect().GetCenter());
                await scry;
                await WaitFrames(120);
                var ui = NCombatRoom.Instance!.Ui;
                var counts = new JsonObject { ["selected"] = selected, ["exhaustSelection"] = exhaustSelection };
                foreach (var (pileType, node) in new (PileType, Control)[]
                    { (PileType.Draw, ui.DrawPile), (PileType.Discard, ui.DiscardPile), (PileType.Exhaust, ui.ExhaustPile) })
                {
                    int actual = pileType.GetPile(player).Cards.Count;
                    string displayed = node.GetNode<Label>("CountContainer/Count").Text;
                    counts[pileType.ToString()] = new JsonObject { ["actual"] = actual, ["displayed"] = displayed };
                    Require(displayed == actual.ToString(), $"{pileType} count differs after Scry: {displayed} vs {actual}.");
                }
                Require(PileType.Draw.GetPile(player).Cards.Count == 3 - selected, "Scry selection removed the wrong number of cards.");
                _checkpoints.Write("release100.scry-count-variants", data: counts);
            }
            await MouseClickAtAsync(exhaustButton.GetGlobalRect().GetCenter());
            await WaitFrames(3);
            Require(NCapstoneContainer.Instance?.CurrentCapstoneScreen is NCardPileScreen,
                "Scry must leave deliberate exhaust-pile inspection available.");
            NCapstoneContainer.Instance!.Close();
        }
        finally { NonInteractiveMode.AutoSlayerCheck = autoslay; }

        await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<FakeMerchant>());
        NFakeMerchant? merchant = null;
        await WaitUntilAsync(() => (merchant = FindDescendant<NFakeMerchant>(_tree.Root)) != null, "Fake Merchant did not load.");
        var container = merchant!.GetNode<Control>("%CharacterContainer");
        var visual = container.GetChildren().OfType<NMerchantCharacter>().Single();
        Require((visual.Scale * container.Scale).IsEqualApprox(Vector2.One), "Fake Merchant enlarged the shop portrait.");
        _checkpoints.Write("release100.fake-merchant", data: new JsonObject
        { ["containerScale"] = container.Scale.ToString(), ["portraitScale"] = visual.Scale.ToString() });
        _checkpoints.Write("release100.completed");
        NGame.Instance.Quit();
    }

    private async Task MouseClickAtAsync(Vector2 point)
    {
        var viewport = _tree.Root;
        viewport.PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
        await WaitFrames(1);
        viewport.PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point,
            ButtonIndex = MouseButton.Left, Pressed = true, ButtonMask = MouseButtonMask.Left }, true);
        await WaitFrames(1);
        viewport.PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point,
            ButtonIndex = MouseButton.Left, Pressed = false }, true);
    }
}
