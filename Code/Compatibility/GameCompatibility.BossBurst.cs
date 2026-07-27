using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Runs;

namespace NinjaSlayer.Code.Compatibility;

internal static partial class GameCompatibility
{
    internal static class BossBurst
    {
        private static readonly MethodInfo? StartDeathAnim = AccessTools.Method(
            typeof(NCreature),
            nameof(NCreature.StartDeathAnim),
            [typeof(bool)]);
        private static readonly MethodInfo? UpdateTrack = AccessTools.Method(
            typeof(NRunMusicController),
            nameof(NRunMusicController.UpdateTrack),
            Type.EmptyTypes);
        private static readonly MethodInfo? CreateSingleDeathVfx = AccessTools.Method(
            typeof(NMonsterDeathVfx),
            nameof(NMonsterDeathVfx.Create),
            [typeof(NCreature), typeof(CancellationToken)]);
        private static readonly MethodInfo? CreateGroupedDeathVfx = AccessTools.Method(
            typeof(NMonsterDeathVfx),
            nameof(NMonsterDeathVfx.Create),
            [typeof(List<NCreature>)]);
        private static readonly MethodInfo? PlayDeathVfx = AccessTools.Method(
            typeof(NMonsterDeathVfx),
            nameof(NMonsterDeathVfx.PlayVfx),
            Type.EmptyTypes);
        private static readonly FieldInfo? CurrentTrack = AccessTools.Field(
            typeof(NRunMusicController),
            "_currentTrack");
        private static readonly FieldInfo? FailedTrack = AccessTools.Field(
            typeof(NRunMusicController),
            "_failedTrack");

        public static IReadOnlyList<CapabilityProbe> GetProbes() =>
        [
            RequiredMember(
                "NCreature.start-death-animation",
                StartDeathAnim,
                "NCreature.StartDeathAnim(bool)"),
            RequiredMember(
                "NRunMusicController.update-track",
                UpdateTrack,
                "NRunMusicController.UpdateTrack()"),
            RequiredMember(
                "NMonsterDeathVfx.create-single",
                CreateSingleDeathVfx,
                "NMonsterDeathVfx.Create(NCreature, CancellationToken)"),
            RequiredMember(
                "NMonsterDeathVfx.create-grouped",
                CreateGroupedDeathVfx,
                "NMonsterDeathVfx.Create(List<NCreature>)"),
            RequiredMember(
                "NMonsterDeathVfx.play",
                PlayDeathVfx,
                "NMonsterDeathVfx.PlayVfx()"),
            RequiredMember(
                "NRunMusicController.current-track",
                CurrentTrack,
                "NRunMusicController._currentTrack"),
            RequiredMember(
                "NRunMusicController.failed-track",
                FailedTrack,
                "NRunMusicController._failedTrack")
        ];

        public static bool TryStopBossMusicImmediately(
            NRunMusicController controller,
            out string reason)
        {
            try
            {
                Node? proxy = controller.GetNodeOrNull<Node>("Proxy");
                if (proxy == null
                    || !proxy.HasMethod("update_global_parameter"))
                {
                    reason = "Run music Proxy cannot reset the global Progress parameter.";
                    return false;
                }

                proxy.Call("update_global_parameter", "Progress", 0f);
                return TryStopCurrentMusicImmediately(proxy, out reason);
            }
            catch (Exception exception)
            {
                reason = exception.Message;
                return false;
            }
        }

        public static bool TryRestoreActMusicWithoutCombatEnd(
            NRunMusicController controller,
            IRunState runState,
            out string reason)
        {
            try
            {
                Node? proxy = controller.GetNodeOrNull<Node>("Proxy");
                if (proxy == null
                    || !proxy.HasMethod("update_global_parameter")
                    || !proxy.HasMethod("update_music"))
                {
                    reason = "Run music Proxy is missing an Act-music restore method.";
                    return false;
                }

                NRunMusicController.MusicSelection? selection =
                    NRunMusicController.ResolveMusic(
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
                    CurrentTrack?.SetValue(controller, selection.Value.Track);
                }
                else
                {
                    CurrentTrack?.SetValue(controller, null);
                }

                FailedTrack?.SetValue(controller, null);

                reason = selection.HasValue
                    ? $"restored {selection.Value.Track} at Progress=0"
                    : "the current Act has no background music";
                return true;
            }
            catch (Exception exception)
            {
                reason = exception.Message;
                return false;
            }
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
}
