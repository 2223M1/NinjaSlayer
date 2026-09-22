using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.AutoSlay.Helpers;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Content;
using NinjaSlayer.Cards.RedesignV1;
using STS2RitsuLib.Audio;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private async Task VerifyPostVictoryCardInput(CancellationToken ct)
    {
        var manager = CombatManager.Instance;
        var state = manager.DebugOnlyGetState()!;
        var player = LocalContext.GetMe(state)!;
        var hand = NCombatRoom.Instance!.Ui.Hand;
        Require(!manager.IsPaused && !manager.PlayerActionsDisabled && !state.HittableEnemies.Any(),
            "Defeating the monsters interrupted player input.");
        await PlayerCmd.SetEnergy(10, player);
        foreach (var card in PileType.Hand.GetPile(player).Cards.ToArray())
            await CardPileCmd.Add(card, PileType.Discard);
        var attack = state.CreateCard<StrikeNinjaSlayerRedesignV1>(player);
        var defense = state.CreateCard<DefendNinjaSlayerRedesignV1>(player);
        await CardPileCmd.Add(attack, PileType.Hand);
        await CardPileCmd.Add(defense, PileType.Hand);
        await WaitFrames(30);
        foreach (var card in new[] { (MegaCrit.Sts2.Core.Models.CardModel)attack, defense })
        {
            var holder = hand.GetCardHolder(card)!;
            holder.EmitSignal(NCardHolder.SignalName.Pressed, holder);
            await WaitFrames(2);
            Require(hand.InCardPlay, "The native hand did not enter card dragging.");
            var play = (NCardPlay)AccessTools.Field(typeof(NPlayerHand), "_currentCardPlay").GetValue(hand)!;
            // The ordinary drag release path cancels an attack with no target,
            // and queues a playable self-targeted skill through TryManualPlay.
            AccessTools.Method(typeof(NCardPlay), "TryPlayCard").Invoke(play, [null]);
            await WaitUntilAsync(() => !hand.InCardPlay, "Native card drag did not clear.", ct);
        }
        await WaitUntilAsync(() => defense.Pile?.Type == PileType.Discard,
            "Queued skill froze after the last enemy died.", ct);
        await RunManager.Instance.ActionExecutor.FinishedExecutingActions();
        Require(attack.Pile?.Type == PileType.Hand && !manager.IsPaused && GetSawatariOptions().Count == 0,
            "Card input after victory paused combat or showed the choice prematurely.");
        _checkpoints.Write("sawatari.post-victory-card-input");
    }

    private async Task EndSawatariPlayerTurn(CancellationToken ct)
    {
        Require(!CombatManager.Instance.IsPaused && GetSawatariOptions().Count == 0,
            "Sawatari took over before the player ended their turn.");
        var button = UiHelper.FindFirst<NEndTurnButton>(NCombatRoom.Instance!)!;
        await WaitUntilAsync(() => button.IsEnabled, "Native End Turn was not available after victory.", ct);
        await UiHelper.Click(button);
    }

    // Bank-only regression: seek within the duel loop as fixture setup, then use
    // the production music controller to request the real authored outro.
    private async Task VerifySawatariOutroBank(string directory, CancellationToken ct)
    {
        var controller = NRunMusicController.Instance!;
        controller.PlayCustomMusic(NinjaSlayerAudio.SawatariCoopMusicEvent);
        controller.UpdateMusicParameter(NinjaSlayerAudio.SawatariCoopPhaseParameter, 3);
        await WaitFrames(30);
        var music = GetSawatariMusic();
        music.Call("set_timeline_position", 300000);
        await WaitFrames(15);
        controller.UpdateMusicParameter(NinjaSlayerAudio.SawatariCoopPhaseParameter, 4);
        await ObserveSawatariOutro(music, directory, "bank-duel", ct);
        controller.StopCustomMusic();
        _checkpoints.Write("sawatari.outro-bank-completed");
    }

    private static GodotObject GetSawatariMusic()
    {
        var description = FmodStudioServer.TryGetEventDescriptionFromGuid("{8bbed2bb-0814-4e59-87f5-5e77e0e32dca}")!;
        return description.Call("get_instance_list").AsGodotArray().Select(v => v.AsGodotObject())
            .Single(instance => instance.Call("get_playback_state").AsInt32() != 2);
    }

    private async Task ObserveSawatariOutro(GodotObject music, string directory, string label, CancellationToken ct)
    {
        var samples = new JsonArray();
        bool stopped = false;
        for (int i = 0; i < 100; i++)
        {
            ct.ThrowIfCancellationRequested();
            await WaitFrames(15);
            int position = music.Call("get_timeline_position").AsInt32();
            stopped = music.Call("get_playback_state").AsInt32() == 2;
            samples.Add(new JsonObject { ["frame"] = i * 15, ["positionMs"] = position, ["stopped"] = stopped });
            if (stopped) break;
        }
        File.WriteAllText(Path.Combine(directory, label + "-timeline.json"), samples.ToJsonString());
        Require(stopped, $"{label}: Sawatari outro remained active; last timeline position {samples.Last()}.");
        _checkpoints.Write("sawatari.outro-completed." + label,
            data: new JsonObject { ["samples"] = samples.Count });
    }
}
