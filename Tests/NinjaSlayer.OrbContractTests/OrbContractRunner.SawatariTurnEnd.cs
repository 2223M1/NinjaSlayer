using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Code.Patches;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static int _sawatariTurnEndCalls;

    private static async Task ObserveSawatariTurnEnd(Task __result)
    {
        await __result;
        _sawatariTurnEndCalls++;
    }

    private async Task VerifySawatariNetworkTurnBarrier(OrbCombat combat, string role, string directory)
    {
        var manager = CombatManager.Instance;
        var run = RunManager.Instance.DebugOnlyGetState()!;
        foreach (var player in run.Players)
        {
            foreach (var power in player.Creature.Powers.ToArray()) await PowerCmd.Remove(power);
            foreach (var card in PileType.Hand.GetPile(player).Cards.ToArray())
                await CardPileCmd.Add(card, PileType.Discard);
            player.PlayerCombatState!.Phase = PlayerTurnPhase.Play;
        }
        _sawatariTurnEndCalls = 0;
        var observer = new Harmony("NinjaSlayer.OrbContracts.SawatariEndTurnObserver");
        observer.Patch(AccessTools.Method(typeof(SawatariTurnEndPatch), nameof(SawatariTurnEndPatch.Postfix)),
            postfix: new HarmonyMethod(GetType(), nameof(ObserveSawatariTurnEnd)));
#if !NINJASLAYER_CHANNEL_STABLE
        // The headless fixture supplies the host's normal turn-start signals;
        // the native preview turn loop consumes the real networked end actions.
        object turn = AccessTools.Field(typeof(CombatManager), "_turnState").GetValue(manager)!;
        var turnType = turn.GetType();
        foreach (string property in new[] { "EndTurnSignalSource", "BeginEnemyTurnSignalSource" })
        {
            var info = turnType.GetProperty(property)!;
            info.SetValue(turn, Activator.CreateInstance(info.PropertyType));
        }
        Task turnLoop = (Task)AccessTools.Method(typeof(CombatManager), "AwaitTurnEndAndSwitchSides")
            .Invoke(manager, [turn])!;
#endif
        try
        {
            File.WriteAllText(Path.Combine(directory, role + ".turn-ready"), "ready");
            await WaitNetwork(() => File.Exists(Path.Combine(directory, "host.turn-ready"))
                && File.Exists(Path.Combine(directory, "client.turn-ready")), "both end-turn fixtures");
            var first = run.Players[0];
            var second = run.Players[1];
            if (first.NetId == _network!.NetId)
                RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new EndPlayerTurnAction(first, first.PlayerCombatState!.TurnNumber));
            await WaitNetwork(() => manager.IsPlayerReadyToEndTurn(first), "first player's native End Turn");
            await ToSignal(GetTree().CreateTimer(0.25), SceneTreeTimer.SignalName.Timeout);
            Require(_sawatariTurnEndCalls == 0 && !manager.IsPaused
                && second.PlayerCombatState!.Phase == PlayerTurnPhase.Play,
                "Sawatari's production turn-end hook ran while the second player could still act.");
            File.WriteAllText(Path.Combine(directory, role + ".first-ended"), "ready");
            await WaitNetwork(() => File.Exists(Path.Combine(directory, "host.first-ended"))
                && File.Exists(Path.Combine(directory, "client.first-ended")), "no early takeover on either peer");
            if (second.NetId == _network.NetId)
                RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new EndPlayerTurnAction(second, second.PlayerCombatState!.TurnNumber));
            await WaitNetwork(() => _sawatariTurnEndCalls == 1, "Sawatari turn-end hook after both native End Turns");
            File.WriteAllText(Path.Combine(directory, role + ".all-ended"), "passed");
            await WaitNetwork(() => File.Exists(Path.Combine(directory, "host.all-ended"))
                && File.Exists(Path.Combine(directory, "client.all-ended")), "both peers crossed the all-player barrier once");
            GD.Print("PASS Sawatari production hook waits for both networked End Turn actions.");
        }
        finally
        {
            observer.UnpatchAll(observer.Id);
        }
    }
}
