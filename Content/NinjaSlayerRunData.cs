using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib;
using STS2RitsuLib.RunData;

namespace NinjaSlayer.Content;

public static class NinjaSlayerRunData
{
    public static PlayerRunSavedData<NinjaSlayerRunState> PlayerState { get; private set; } = null!;

    public static void Register(string modId)
    {
        var store = RitsuLibFramework.GetRunSavedDataStore(modId);
        PlayerState = store.RegisterPerPlayer(
            key: "ninja_slayer_run_state",
            defaultFactory: () => new NinjaSlayerRunState(),
            options: new RunSavedDataOptions
            {
                WritePolicy = RunSavedDataWritePolicy.WhenNonDefault
            });
    }

    public static void MarkPendingAncientEntranceAnimation(Player player)
    {
        PlayerState.Modify(player, state => state.PendingAncientEntranceAnimation = true);
    }

    public static void SnapshotEventValidation(RunState runState, bool enabled)
    {
        foreach (Player player in runState.Players)
        {
            PlayerState.Modify(player, state => state.EventValidationEnabled = enabled);
        }
    }

    public static bool IsEventValidationEnabled(IRunState runState) =>
        runState.Players.Count == 1
        && PlayerState.Get(runState.Players[0]).EventValidationEnabled;

    public static bool HasPendingAncientEntranceAnimation(Player player) =>
        PlayerState.Get(player).PendingAncientEntranceAnimation;

    public static bool ConsumePendingAncientEntranceAnimation(Player player)
    {
        if (!PlayerState.Get(player).PendingAncientEntranceAnimation)
        {
            return false;
        }

        PlayerState.Modify(player, state => state.PendingAncientEntranceAnimation = false);
        return true;
    }

    public static bool HasCompletedBossGreeting(Player player, string roomKey) =>
        PlayerState.Get(player).CompletedBossGreetingRoomKeys.Contains(roomKey, StringComparer.Ordinal);

    public static void MarkBossGreetingCompleted(Player player, string roomKey)
    {
        PlayerState.Modify(player, state =>
        {
            if (!state.CompletedBossGreetingRoomKeys.Contains(roomKey, StringComparer.Ordinal))
            {
                state.CompletedBossGreetingRoomKeys.Add(roomKey);
            }

            state.PendingAncientEntranceAnimation = false;
        });
    }
}
