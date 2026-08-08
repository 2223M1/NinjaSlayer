using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Code.Transition;
using STS2RitsuLib.Patching.Models;
using STS2RitsuLib.Utils.HarmonyIl;

namespace NinjaSlayer.Code.Patches;

public sealed class NinjaSlayerTransitionAssetLoadConcurrencyPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_transition_asset_load_concurrency";

    public static string Description =>
        "Limit concurrent threaded asset requests while the NinjaSlayer transition is visible.";

    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
        GameCompatibility.AssetLoading.ProcessLoadingQueue is { } target
            ? [new(target.DeclaringType!, target.Name)]
            : [];

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        const string operation = "NinjaSlayer transition asset concurrency limit";
        var rewriter = HarmonyIlRewriter.From(instructions);
        MethodInfo replacement = GameCompatibility.AssetLoading.ConcurrentAssetLoadLimit
            ?? throw new MissingMethodException(
                typeof(NinjaSlayerTransitionLoadSmoothing).FullName,
                nameof(NinjaSlayerTransitionLoadSmoothing.GetConcurrentAssetLoadLimit));
        HarmonyIlRewriteReport report = rewriter.ReplaceEach(
            operation,
            (code, index) => GameCompatibility.AssetLoading.IsLoadingCountLimitSite(
                code,
                index,
                128),
            (_, _) => [HarmonyIl.Call(replacement)],
            code => code.Any(HarmonyIl.IsCall(replacement)));
        return rewriter.InstructionsChecked(report);
    }
}

public sealed class NinjaSlayerTransitionAssetFinalizePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_transition_asset_finalize_batching";

    public static string Description =>
        "Finalize threaded resources in small batches while the NinjaSlayer transition is visible.";

    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
        GameCompatibility.AssetLoading.FinalizeLoading is { } target
            ? [new(target.DeclaringType!, target.Name)]
            : [];

    public static bool Prefix(AssetLoadingSession __instance)
    {
        if (!NinjaSlayerTransitionLoadSmoothing.IsAnimationPlaying)
        {
            return true;
        }

        if (!GameCompatibility.AssetLoading.TryGetFinalizing(__instance, out Queue<string>? finalizing))
        {
            return true;
        }

        long batchStartedAt = Stopwatch.GetTimestamp();
        var finalized = 0;
        while ((finalized < NinjaSlayerTransitionLoadSmoothing.FinalizeBatchMinimum
                || Stopwatch.GetElapsedTime(batchStartedAt)
                    < NinjaSlayerTransitionLoadSmoothing.FinalizeBatchBudget)
               && finalizing.TryDequeue(out string? path))
        {
            Resource? resource = ResourceLoader.LoadThreadedGet(path);
            GameCompatibility.AssetLoading.Cache(__instance, resource, path);
            finalized++;
        }

        return false;
    }
}

public static class NinjaSlayerTransitionGcDeferralPatch
{
    private const string PatchIdPrefix = "ninjaslayer_transition_preload_gc_deferral";

    public static DynamicPatchInfo[] CreateDynamicPatches()
    {
        if (!GameCompatibility.AssetLoading.TryResolvePreloadStateMachines(
                out GameCompatibility.RuntimePatchTarget[] targets,
                out string missingMember))
        {
            throw new MissingMethodException(missingMember);
        }

        var harmonyTranspiler = new HarmonyMethod(
            typeof(NinjaSlayerTransitionGcDeferralPatch),
            nameof(Transpiler));
        return targets.Select(target => new DynamicPatchInfo(
                $"{PatchIdPrefix}_{target.IdSuffix}",
                target.Method,
                transpiler: harmonyTranspiler,
                isCritical: true,
                description: $"Defer forced GC in PreloadManager {target.IdSuffix}."))
            .ToArray();
    }

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        const string operation = "NinjaSlayer transition GC deferral";
        MethodInfo gcCollect = GameCompatibility.AssetLoading.GcCollect
            ?? throw new MissingMethodException(typeof(GC).FullName, nameof(GC.Collect));
        MethodInfo safeCollect = GameCompatibility.AssetLoading.SafeCollect
            ?? throw new MissingMethodException(
                typeof(NinjaSlayerTransitionLoadSmoothing).FullName,
                nameof(NinjaSlayerTransitionLoadSmoothing.CollectWhenSafe));
        var rewriter = HarmonyIlRewriter.From(instructions);
        HarmonyIlRewriteReport report = rewriter.RedirectCalls(
            operation,
            called => called == gcCollect ? safeCollect : null,
            code => code.Any(HarmonyIl.IsCall(safeCollect)));
        return rewriter.InstructionsChecked(report);
    }
}
