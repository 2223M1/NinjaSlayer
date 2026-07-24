using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Code.Transition;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class NinjaSlayerTransitionAssetFinalizePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_transition_asset_finalize_batching";

    public static string Description =>
        "Finalize threaded resources in small batches while the NinjaSlayer transition is visible.";

    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(AssetLoadingSession), "FinalizeLoading")];

    public static bool Prefix(AssetLoadingSession __instance)
    {
        if (!NinjaSlayerTransitionLoadSmoothing.IsAnimationPlaying)
        {
            return true;
        }

        if (!GameCompatibility.AssetLoading.TryGetFinalizing(__instance, out Queue<string>? finalizing)
            || finalizing is null)
        {
            return true;
        }

        long batchStartedAt = Stopwatch.GetTimestamp();
        var finalized = 0;
        while (finalized < NinjaSlayerTransitionLoadSmoothing.FinalizeBatchSize &&
               finalizing.TryDequeue(out string? path))
        {
            Resource? resource = ResourceLoader.LoadThreadedGet(path);
            GameCompatibility.AssetLoading.Cache(__instance, resource, path);
            finalized++;
        }

        NinjaSlayerTransitionLoadSmoothing.RecordFinalizeBatch(
            finalized,
            Stopwatch.GetElapsedTime(batchStartedAt));
        return false;
    }
}

public sealed class NinjaSlayerTransitionGcDeferralPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_transition_preload_gc_deferral";

    public static string Description =>
        "Coalesce forced run-asset garbage collection across the active NinjaSlayer transition session.";

    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() => TryResolveTargets(out ModPatchTarget[] targets, out _)
        ? targets
        : [];

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (CodeInstruction instruction in instructions)
        {
            if (GameCompatibility.AssetLoading.GcCollect is { } gcCollect
                && GameCompatibility.AssetLoading.SafeCollect is { } safeCollect
                && instruction.Calls(gcCollect))
            {
                instruction.operand = safeCollect;
            }

            yield return instruction;
        }
    }

    private static bool TryResolveTargets(out ModPatchTarget[] targets, out string missingMember)
    {
        if (!GameCompatibility.AssetLoading.TryResolvePreloadStateMachines(
                out Type[] stateMachines,
                out missingMember))
        {
            targets = [];
            return false;
        }

        targets = stateMachines
            .Select(stateMachine => new ModPatchTarget(stateMachine, "MoveNext"))
            .ToArray();
        return true;
    }
}
