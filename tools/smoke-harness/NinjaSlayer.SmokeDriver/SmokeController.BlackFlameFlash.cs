using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Cards.RedesignV1;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private async Task VerifyBlackFlameFlash(ICombatState combat, Player player, Creature target)
    {
        var choice = new BlockingPlayerChoiceContext();
        CardModel[] originalHand = PileType.Hand.GetPile(player).Cards.ToArray();
        foreach (CardModel card in originalHand) await CardPileCmd.Add(card, PileType.Discard);
        var flames = new[] { combat.CreateCard<BlackFlameRedesignV1>(player), combat.CreateCard<BlackFlameRedesignV1>(player) };
        foreach (var flame in flames) await CardPileCmd.Add(flame, PileType.Hand);
        var attack = combat.CreateCard<StrikeNinjaSlayerRedesignV1>(player);
        await CardPileCmd.Add(attack, PileType.Hand);
        var room = NCombatRoom.Instance!;
        await room.ToSignal(room.GetTree(), SceneTree.SignalName.ProcessFrame);
        var flashes = flames.Select(flame => ((NHandCardHolder)room.Ui.Hand.GetCardHolder(flame)!).GetNode<Control>("Flash")).ToArray();
        bool[] observed = new bool[flames.Length];
        bool captured = false;
        Task play = CardCmd.AutoPlay(choice, attack, target);
        while (!play.IsCompleted)
        {
            for (int i = 0; i < flashes.Length; i++)
                observed[i] |= flashes[i].IsVisibleInTree() && flashes[i].Modulate.A > 0.05f;
            if (!captured && observed.All(value => value))
            {
                await CapturePresentation("black-flame-native-flash");
                captured = true;
            }
            await room.ToSignal(room.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        await play;
        Require(observed.All(value => value), "Every triggered hand Black Flame must show the native Flash animation.");
        Require(flames.All(flame => flame.Pile?.Type == PileType.Hand), "Black Flame feedback moved a card out of hand.");
        await Task.Delay(600);
        Require(flashes.All(flash => flash.Modulate.A < 0.01f), "Native Black Flame flash did not fade out.");
        foreach (CardModel flame in flames) await CardPileCmd.Add(flame, PileType.Discard);
        foreach (CardModel card in originalHand) await CardPileCmd.Add(card, PileType.Hand);
        await CreatureCmd.SetCurrentHp(target, 1000);
        _checkpoints.Write("black-flame.native-flash-completed");
    }
}
