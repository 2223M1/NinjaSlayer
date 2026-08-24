using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class BossBurstMusicSession
{
    private static readonly FieldInfo CurrentTrack =
        AccessTools.Field(typeof(NRunMusicController), "_currentTrack")
        ?? throw new MissingFieldException(typeof(NRunMusicController).FullName, "_currentTrack");
#if NINJASLAYER_CHANNEL_PREVIEW
    private static readonly FieldInfo FailedTrack =
        AccessTools.Field(typeof(NRunMusicController), "_failedTrack")
        ?? throw new MissingFieldException(typeof(NRunMusicController).FullName, "_failedTrack");
#endif

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

        if (TryStopBossMusicImmediately(controller, out string reason))
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
        TryRestoreActMusicWithoutCombatEnd(controller, runState, out reason);

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
            else if (!TryRestoreActMusicWithoutCombatEnd(
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

    private static bool TryStopBossMusicImmediately(
        NRunMusicController controller,
        out string reason)
    {
        Node? proxy = controller.GetNodeOrNull<Node>("Proxy");
        if (proxy == null || !proxy.HasMethod("update_global_parameter"))
        {
            reason = "Run music Proxy cannot reset the global Progress parameter.";
            return false;
        }

        proxy.Call("update_global_parameter", "Progress", 0f);
        return TryStopCurrentMusicImmediately(proxy, out reason);
    }

    private static bool TryRestoreActMusicWithoutCombatEnd(
        NRunMusicController controller,
        IRunState runState,
        out string reason)
    {
        Node? proxy = controller.GetNodeOrNull<Node>("Proxy");
        if (proxy == null
            || !proxy.HasMethod("update_global_parameter")
            || !proxy.HasMethod("update_music"))
        {
            reason = "Run music Proxy is missing an Act-music restore method.";
            return false;
        }

        NRunMusicController.MusicSelection? selection = NRunMusicController.ResolveMusic(
            currentTrack: null,
            runState.Act.BgMusicOptions,
            runState.Act.MusicBankPaths,
            runState.Rng.Seed);
        proxy.Call("update_global_parameter", "Progress", 0f);
        if (!TryStopCurrentMusicImmediately(proxy, out string stopReason))
        {
            reason = stopReason;
            return false;
        }

        if (selection.HasValue)
        {
            proxy.Call("update_music", selection.Value.Track);
            CurrentTrack.SetValue(controller, selection.Value.Track);
        }
        else
        {
            CurrentTrack.SetValue(controller, null);
        }

#if NINJASLAYER_CHANNEL_PREVIEW
        FailedTrack.SetValue(controller, null);
#endif
        reason = selection.HasValue
            ? $"restored {selection.Value.Track} at Progress=0"
            : "the current Act has no background music";
        return true;
    }

    private static bool TryStopCurrentMusicImmediately(Node proxy, out string reason)
    {
        Variant currentEvent = proxy.Get("_musicEv");
        GodotObject? musicEvent = currentEvent.VariantType == Variant.Type.Object
            ? currentEvent.AsGodotObject()
            : null;
        if (musicEvent != null && GodotObject.IsInstanceValid(musicEvent))
        {
            if (!musicEvent.HasMethod("stop") || !musicEvent.HasMethod("release"))
            {
                reason = "The active FMOD music event cannot be stopped immediately.";
                return false;
            }

            // FMOD_STUDIO_STOP_IMMEDIATE is 1. Vanilla stop_music() uses 0
            // (ALLOWFADEOUT), which lets the Boss defeat stinger begin before release.
            musicEvent.Call("stop", 1);
            musicEvent.Call("release");
        }

        proxy.Set("_musicEv", default(Variant));
        reason = "Boss music stopped immediately.";
        return true;
    }
}
