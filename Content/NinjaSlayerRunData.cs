using MegaCrit.Sts2.Core.Entities.Players;
using STS2RitsuLib;
using STS2RitsuLib.RunData;

namespace NinjaSlayer.Content;

public sealed class NinjaSlayerRunState
{
    public bool PendingAncientEntranceAnimation { get; set; }

    public List<string> CompletedBossGreetingRoomKeys { get; set; } = [];
}

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
        PlayerState.Get(player).CompletedBossGreetingRoomKeys?.Contains(roomKey, StringComparer.Ordinal) ?? false;

    public static void MarkBossGreetingCompleted(Player player, string roomKey)
    {
        PlayerState.Modify(player, state =>
        {
            state.CompletedBossGreetingRoomKeys ??= [];
            if (!state.CompletedBossGreetingRoomKeys.Contains(roomKey, StringComparer.Ordinal))
            {
                state.CompletedBossGreetingRoomKeys.Add(roomKey);
            }

            state.PendingAncientEntranceAnimation = false;
        });
    }
}
