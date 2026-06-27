using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using STS2RitsuLib.Interop;

namespace NinjaSlayer.Code.Interop;

/// <summary>
/// Optional Watcher mod linkage for Scry. When Watcher is loaded, NinjaSlayer Scry
/// dispatches the same combat event so Watcher cards and powers react.
/// </summary>
[ModInterop("Watcher", "Watcher.Code.Events.WatcherHook")]
public static class WatcherScryHookInterop
{
    public static bool IsReady => false;

    public static Task OnScryed(PlayerChoiceContext ctx, Player player, int amount, int discardedAmount)
        => Task.CompletedTask;
}
