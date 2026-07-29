using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class BossBurstMusicSession
{
    public static bool Begin(NCombatRoom sceneRoom)
    {
        if (!BossBurstParticipationRegistry.TryClaimBossMusicStop(sceneRoom))
        {
            return false;
        }

        NRunMusicController? controller = NRunMusicController.Instance;
        if (controller == null)
        {
            Entry.Logger.Warn("Boss burst could not stop Boss music because the run music controller is unavailable.");
            return true;
        }

        if (GameCompatibility.BossBurst.TryStopBossMusicImmediately(controller, out string reason))
        {
            Entry.Logger.Info($"Boss burst stopped Boss music before the death presentation; {reason}.");
        }
        else
        {
            Entry.Logger.Warn($"Boss burst could not stop Boss music before the death presentation: {reason}");
        }

        return true;
    }

    public static bool Complete(
        NRunMusicController controller,
        IRunState runState,
        out string reason) =>
        GameCompatibility.BossBurst.TryRestoreActMusicWithoutCombatEnd(
            controller,
            runState,
            out reason);

    public static void Rollback(CombatRoom room, IRunState runState)
    {
        NRunMusicController? controller = NRunMusicController.Instance;
        if (controller == null)
        {
            return;
        }

        try
        {
            if (room.Encounter.HasBgm)
            {
                controller.PlayCustomMusic(room.Encounter.CustomBgm);
                controller.UpdateTrack();
            }
            else if (!GameCompatibility.BossBurst.TryRestoreActMusicWithoutCombatEnd(
                         controller,
                         runState,
                         out string reason))
            {
                Entry.Logger.Warn($"Boss burst Act music rollback degraded: {reason}");
            }
        }
        catch (Exception exception)
        {
            Entry.Logger.Warn($"Boss burst music rollback failed: {exception.Message}");
        }
    }
}
