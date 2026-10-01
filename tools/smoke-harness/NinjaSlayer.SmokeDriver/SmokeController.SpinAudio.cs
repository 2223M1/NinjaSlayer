using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Cards.Standard;
using System.Text.Json.Nodes;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    // Run the real card and bank without starting a theater recorder.
    private async Task VerifySpinAudioAsync(Player player)
    {
        var room = NCombatRoom.Instance!;
        var combat = player.Creature.CombatState!;
        var target = combat.HittableEnemies.First();
        target.SetMaxHpInternal(5000);
        target.SetCurrentHpInternal(5000);
        await PlayerCmd.SetEnergy(30m, player);
        var card = combat.CreateCard<TornadoFist>(player);
        await CardPileCmd.Add(card, PileType.Hand);
        Task play = CardCmd.AutoPlay(new BlockingPlayerChoiceContext(), card, target);
        Node? owner = null;
        await WaitUntilAsync(() => (owner = room.GetChildren().FirstOrDefault(n => n.GetType().Name == "SpinComboAudio")) != null,
            "Tornado did not create its FMOD owner.");
        ulong id = owner!.GetInstanceId();
        object handle = AccessTools.Field(owner.GetType(), "_audio").GetValue(owner)!;
        var raw = (GodotObject)AccessTools.Property(handle.GetType(), "RawInstance").GetValue(handle)!;
        int Position() => raw.Call("get_timeline_position").AsInt32();
        bool Released() => (bool)AccessTools.Property(handle.GetType(), "IsReleased").GetValue(handle)!;
        var paused = AccessTools.Property(typeof(CombatManager), "IsPaused");
        await WaitFrames(20);
        int pauseDrift;
        paused.SetValue(CombatManager.Instance, true);
        try
        {
            await WaitFrames(5);
            int before = Position();
            await WaitFrames(30);
            pauseDrift = Math.Abs(Position() - before);
            Require(pauseDrift <= 1, "Paused Tornado FMOD timeline advanced.");
        }
        finally { paused.SetValue(CombatManager.Instance, false); }
        int previous = Position(), wraps = 0;
        bool outro = false;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!Released() && timer.Elapsed.TotalSeconds < 40)
        {
            int position = Position();
            bool finishing = (bool)AccessTools.Field(owner.GetType(), "_finishing").GetValue(owner)!;
            if (!finishing && previous > 2300 && position is >= 1300 and < 1600) wraps++;
            outro |= position >= 2554;
            previous = position;
            await WaitFrames(1);
        }
        Require(play.IsCompleted && Released(), "Tornado action or FMOD owner did not finish.");
        await play;
        Require(wraps > 0 && outro, "Tornado failed to sustain its loop and play the outro.");
        Require(TornadoAudioReleaseObserver.States.Remove(id, out var release) && release.State == 2,
            "Tornado released before native FMOD STOPPED.");
        _checkpoints.Write("release039.spin-audio", data: new JsonObject
        { ["loopWraps"] = wraps, ["pauseDriftMs"] = pauseDrift, ["outro"] = outro,
          ["nativeStateAtRelease"] = release.State, ["released"] = Released() });
    }
}
