using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Scripts;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class BossBurstCombatEndMusicPatch : IPatchMethod
{
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
}

public sealed class BossBurstSingleDeathFadePatch : IPatchMethod
{
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
        if (!BossBurstParticipationRegistry.ShouldSuppressDeathFade(creatureNode))
        {
            return true;
        }

        __result = null;
        return false;
    }
}

public sealed class BossBurstGroupedDeathFadePatch : IPatchMethod
{
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
            Entry.Logger.Info(
                "Boss burst suppressed an all-participant grouped death VFX "
                + "while preserving the vanilla caller's creature-node cleanup.");
        }

        return true;
    }

    public static void Postfix(NMonsterDeathVfx? __result, bool __state)
    {
        if (!__state || __result == null)
        {
            return;
        }

        BossBurstDeathFadeRegistry.MarkPlaybackSuppressed(__result);
    }
}

public sealed class BossBurstDeathFadePlaybackPatch : IPatchMethod
{
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

        __instance.QueueFreeSafely();
        __result = Task.CompletedTask;
        return false;
    }
}
