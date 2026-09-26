using System.Text.Json.Nodes;
using Godot;
using MegaCrit.Sts2.Core.AutoSlay.Helpers;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Relics;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private async Task VerifyExhaustPileUi(ICombatState combat, Player player)
    {
        var previous = SaveManager.Instance.PrefsSave.FastMode;
        foreach (FastModeType speed in Enum.GetValues<FastModeType>())
        {
            SaveManager.Instance.PrefsSave.FastMode = speed;
            var tea = combat.CreateCard<ChadoEnergyRedesignV1>(player);
            await CardPileCmd.Add(tea, PileType.Hand);
            await CardCmd.AutoPlay(new BlockingPlayerChoiceContext(), tea, player.Creature);
            var button = NCombatRoom.Instance!.Ui.ExhaustPile;
            int count = PileType.Exhaust.GetPile(player).Cards.Count;
            await WaitUntilAsync(() => button.GetNode<Label>("CountContainer/Count").Text == count.ToString(),
                $"Exhaust badge differs from the actual pile at {speed} speed.");
            await UiHelper.Click(button);
            NCardPileScreen? screen = null;
            await WaitUntilAsync(() => (screen = FindDescendant<NCardPileScreen>(_tree.Root)) is not null,
                "Exhaust card list did not open.");
            Require(screen!.Pile.Type == PileType.Exhaust && screen.Pile.Cards.Count == count,
                "Opened exhaust pile and badge disagree.");
            SaveScreenshot(
                Path.Combine(Path.GetDirectoryName(_configuration.CheckpointPath)!, $"exhaust-{speed}.png"));
            await UiHelper.Click(screen.GetNode<NButton>("BackButton"));
            await WaitFrames(2);
            _checkpoints.Write("exhaust.badge-list-match", data: new JsonObject { ["speed"] = speed.ToString(), ["count"] = count });
        }
        SaveManager.Instance.PrefsSave.FastMode = previous;
        var anchor = new Control { Position = new Vector2(600, 200), Size = new Vector2(80, 80) };
        NCombatRoom.Instance!.Ui.AddChild(anchor);
        foreach (var (name, tips) in new (string, IEnumerable<IHoverTip>)[]
        {
            ("irc", ModelDb.Relic<IrcTerminalRelic>().HoverTips),
            ("starter-tea", ModelDb.Relic<ChadoBreathingRelic>().HoverTips),
            ("karate", ModelDb.Card<KarateStraightRedesignV1>().HoverTips)
        })
        {
            var set = NHoverTipSet.CreateAndShow(anchor, tips, HoverTipAlignment.Right);
            Require(set is not null && set.Visible, $"{name} side tooltip did not appear.");
            await WaitFrames(5);
            if (name != "karate")
                Require(FindDescendant<NCard>(set!) is not null, $"{name} generated card preview did not render.");
            SaveScreenshot(
                Path.Combine(Path.GetDirectoryName(_configuration.CheckpointPath)!, $"tooltip-{name}.png"));
            NHoverTipSet.Remove(anchor);
            _checkpoints.Write("tooltip.rendered", data: new JsonObject { ["name"] = name });
        }
        anchor.QueueFree();
    }
}
