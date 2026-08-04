using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace NinjaSlayer.Code.Compatibility;

internal static partial class GameCompatibility
{
    internal static class ArchitectVictory
    {
        public static Task Complete(Player owner, NCombatRoom room)
        {
#if NINJASLAYER_LEGACY_ARCHITECT_VICTORY_COMPLETION
            if (owner.RunState.Players.Count > 1)
            {
                room.SetWaitingForOtherPlayersOverlayVisible(visible: true);
            }

            RunManager.Instance.ActChangeSynchronizer.SetLocalPlayerReady();
            return Task.CompletedTask;
#else
            return RunManager.Instance.WinRun();
#endif
        }
    }
}
