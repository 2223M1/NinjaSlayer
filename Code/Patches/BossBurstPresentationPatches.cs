using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Scripts;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class BossBurstCombatEndMusicPatch : IPatchMethod
{
    private static int _runtimeFailureLogged;

    public static string PatchId => "ninjaslayer_boss_burst_combat_end_music";
    public static string Description =>
        "Replace the exploding Boss CombatEnd music transition without triggering vanilla defeat stingers.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NRunMusicController), nameof(NRunMusicController.UpdateTrack), Type.EmptyTypes)
    ];

    [HarmonyPriority(Priority.First)]
    public static bool Prefix(NRunMusicController __instance)
    {
        try
        {
            BossBurstCombatEndMusicDecision decision =
                BossBurstParticipationRegistry.ResolveCombatEndMusic(out IRunState? runState);
            if (decision == BossBurstCombatEndMusicDecision.PassThrough)
            {
                return true;
            }

            if (decision == BossBurstCombatEndMusicDecision.SuppressAndRestoreActMusic
                && runState != null)
            {
                if (BossBurstMusicSession.Complete(
                        __instance,
                        runState,
                        out string reason))
                {
                    Entry.Logger.Info($"Boss burst combat-end stingers suppressed; {reason}.");
                }
                else
                {
                    Entry.Logger.Warn(
                        $"Boss burst Act music restore failed; keeping CombatEnd stingers suppressed "
                        + $"without invoking vanilla fade-out: {reason}");
                }
            }

            return false;
        }
        catch (Exception exception)
        {
            bool suppressVanilla =
                BossBurstParticipationRegistry.ShouldSuppressCombatEndMusicAfterFailure();
            if (Interlocked.Exchange(ref _runtimeFailureLogged, 1) == 0)
            {
                Entry.Logger.Error(
                    $"Boss burst CombatEnd music Patch failed; "
                    + (suppressVanilla
                        ? "keeping the registered Boss stinger suppressed"
                        : "preserving vanilla UpdateTrack")
                    + $": {exception}");
            }

            return !suppressVanilla;
        }
    }
}

public sealed class BossBurstSingleDeathFadePatch : IPatchMethod
{
    private static int _runtimeFailureLogged;

    public static string PatchId => "ninjaslayer_boss_burst_single_death_fade";
    public static string Description =>
        "Suppress vanilla death fading for a Boss owned by the NinjaSlayer burst presentation.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(NMonsterDeathVfx),
            nameof(NMonsterDeathVfx.Create),
            [typeof(NCreature), typeof(CancellationToken)])
    ];

    public static bool Prefix(NCreature creatureNode, ref NMonsterDeathVfx? __result)
    {
        try
        {
            if (!BossBurstParticipationRegistry.ShouldSuppressDeathFade(creatureNode))
            {
                return true;
            }

            __result = null;
            return false;
        }
        catch (Exception exception)
        {
            LogFailureOnce(ref _runtimeFailureLogged, "single", exception);
            return true;
        }
    }

    internal static void LogFailureOnce(
        ref int failureLogged,
        string overload,
        Exception exception)
    {
        if (Interlocked.Exchange(ref failureLogged, 1) == 0)
        {
            Entry.Logger.Error(
                $"Boss burst {overload} death-fade Patch failed; "
                + $"preserving vanilla death VFX: {exception}");
        }
    }
}

public sealed class BossBurstGroupedDeathFadePatch : IPatchMethod
{
    private static int _runtimeFailureLogged;
    private static int _allParticipantsWarningLogged;

    public static string PatchId => "ninjaslayer_boss_burst_grouped_death_fade";
    public static string Description =>
        "Suppress a grouped vanilla death fade when it contains a Boss burst participant.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(NMonsterDeathVfx),
            nameof(NMonsterDeathVfx.Create),
            [typeof(List<NCreature>)])
    ];

    public static bool Prefix(ref List<NCreature> creatureNodes, out bool __state)
    {
        __state = false;
        try
        {
            List<NCreature> remaining = creatureNodes
                .Where(creature => !BossBurstParticipationRegistry.ShouldSuppressDeathFade(creature))
                .ToList();
            int participantCount = creatureNodes.Count - remaining.Count;
            BossBurstGroupedDeathFadeDecision decision =
                BossBurstPresentationPolicy.ResolveGroupedDeathFade(
                    creatureNodes.Count,
                    participantCount);
            if (decision == BossBurstGroupedDeathFadeDecision.PassThrough)
            {
                return true;
            }

            if (decision == BossBurstGroupedDeathFadeDecision.FilterParticipants)
            {
                // The caller retains its original list for post-VFX node cleanup; only
                // the visual capture input is narrowed to creatures not owned by the burst.
                creatureNodes = remaining;
            }
            else
            {
                __state = true;
                if (Interlocked.Exchange(ref _allParticipantsWarningLogged, 1) == 0)
                {
                    Entry.Logger.Info(
                        "Boss burst suppressed an all-participant grouped death VFX "
                        + "while preserving the vanilla caller's creature-node cleanup.");
                }
            }

            return true;
        }
        catch (Exception exception)
        {
            BossBurstSingleDeathFadePatch.LogFailureOnce(
                ref _runtimeFailureLogged,
                "grouped",
                exception);
            return true;
        }
    }

    public static void Postfix(NMonsterDeathVfx? __result, bool __state)
    {
        if (!__state || __result == null)
        {
            return;
        }

        try
        {
            BossBurstDeathFadeRegistry.MarkPlaybackSuppressed(__result);
        }
        catch (Exception exception)
        {
            BossBurstSingleDeathFadePatch.LogFailureOnce(
                ref _runtimeFailureLogged,
                "grouped playback registration",
                exception);
        }
    }
}

public sealed class BossBurstDeathFadePlaybackPatch : IPatchMethod
{
    private static int _runtimeFailureLogged;

    public static string PatchId => "ninjaslayer_boss_burst_death_fade_playback";
    public static string Description =>
        "Complete an all-Boss grouped death VFX without playing enemy_fade.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NMonsterDeathVfx), nameof(NMonsterDeathVfx.PlayVfx), Type.EmptyTypes)
    ];

    public static bool Prefix(NMonsterDeathVfx __instance, ref Task __result)
    {
        if (!BossBurstDeathFadeRegistry.ConsumePlaybackSuppression(__instance))
        {
            return true;
        }

        try
        {
            __instance.QueueFreeSafely();
        }
        catch (Exception exception)
        {
            BossBurstSingleDeathFadePatch.LogFailureOnce(
                ref _runtimeFailureLogged,
                "grouped playback cleanup",
                exception);
        }

        __result = Task.CompletedTask;
        return false;
    }
}
