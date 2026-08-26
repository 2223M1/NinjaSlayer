using System.Diagnostics;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using NinjaSlayer.Code.Transition;
using STS2RitsuLib.Patching.Models;
using STS2RitsuLib.Utils.HarmonyIl;

namespace NinjaSlayer.Code.Patches;

public sealed class NinjaSlayerTransitionAssetLoadConcurrencyPatch : IPatchMethod
{
    private static readonly FieldInfo Loading =
        AccessTools.Field(typeof(AssetLoadingSession), "_loading")
        ?? throw new MissingFieldException(typeof(AssetLoadingSession).FullName, "_loading");
    private static readonly MethodInfo LoadingCount =
        AccessTools.PropertyGetter(typeof(Queue<string>), nameof(Queue<string>.Count))
        ?? throw new MissingMethodException(typeof(Queue<string>).FullName, "get_Count");
    private static readonly MethodInfo ProcessLoadingQueue =
        AccessTools.Method(typeof(AssetLoadingSession), "ProcessLoadingQueue")
        ?? throw new MissingMethodException(
            typeof(AssetLoadingSession).FullName,
            "ProcessLoadingQueue");
    private static readonly MethodInfo ConcurrentAssetLoadLimit = AccessTools.Method(
        typeof(NinjaSlayerTransitionLoadSmoothing),
        nameof(NinjaSlayerTransitionLoadSmoothing.GetConcurrentAssetLoadLimit))
        ?? throw new MissingMethodException(
            typeof(NinjaSlayerTransitionLoadSmoothing).FullName,
            nameof(NinjaSlayerTransitionLoadSmoothing.GetConcurrentAssetLoadLimit));

    public static string PatchId => "ninjaslayer_transition_asset_load_concurrency";

    public static string Description =>
        "Limit concurrent threaded asset requests while the NinjaSlayer transition is visible.";

    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
        [new(ProcessLoadingQueue.DeclaringType!, ProcessLoadingQueue.Name)];

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        const string operation = "NinjaSlayer transition asset concurrency limit";
        var rewriter = HarmonyIlRewriter.From(instructions);
        HarmonyIlRewriteReport report = rewriter.ReplaceEach(
            operation,
            (code, index) => IsLoadingCountLimitSite(code, index, 128),
            (_, _) => [HarmonyIl.Call(ConcurrentAssetLoadLimit)],
            code => code.Any(HarmonyIl.IsCall(ConcurrentAssetLoadLimit)));
        return rewriter.InstructionsChecked(report);
    }

    private static bool IsLoadingCountLimitSite(
        IReadOnlyList<CodeInstruction> code,
        int index,
        int expectedLimit) =>
        index >= 3
        && code[index].LoadsConstant(expectedLimit)
        && HarmonyLib.CodeInstructionExtensions.IsLdarg(code[index - 3], 0)
        && code[index - 2].LoadsField(Loading)
        && code[index - 1].Calls(LoadingCount);
}

public sealed class NinjaSlayerTransitionAssetFinalizePatch : IPatchMethod
{
    private static readonly FieldInfo Finalizing =
        AccessTools.Field(typeof(AssetLoadingSession), "_finalizing")
        ?? throw new MissingFieldException(typeof(AssetLoadingSession).FullName, "_finalizing");
    private static readonly MethodInfo AddToCache =
        AccessTools.Method(typeof(AssetLoadingSession), "AddToCache")
        ?? throw new MissingMethodException(typeof(AssetLoadingSession).FullName, "AddToCache");
    private static readonly MethodInfo FinalizeLoading =
        AccessTools.Method(typeof(AssetLoadingSession), "FinalizeLoading")
        ?? throw new MissingMethodException(
            typeof(AssetLoadingSession).FullName,
            "FinalizeLoading");

    public static string PatchId => "ninjaslayer_transition_asset_finalize_batching";

    public static string Description =>
        "Finalize threaded resources in small batches while the NinjaSlayer transition is visible.";

    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
        [new(FinalizeLoading.DeclaringType!, FinalizeLoading.Name)];

    public static bool Prefix(AssetLoadingSession __instance)
    {
        if (!NinjaSlayerTransitionLoadSmoothing.IsAnimationPlaying)
        {
            return true;
        }

        Queue<string> finalizing = Finalizing.GetValue(__instance) as Queue<string>
            ?? throw new InvalidOperationException(
                "AssetLoadingSession._finalizing is not an initialized queue.");

        long batchStartedAt = Stopwatch.GetTimestamp();
        var finalized = 0;
        while ((finalized < NinjaSlayerTransitionLoadSmoothing.FinalizeBatchMinimum
                || Stopwatch.GetElapsedTime(batchStartedAt)
                    < NinjaSlayerTransitionLoadSmoothing.FinalizeBatchBudget)
               && finalizing.TryDequeue(out string? path))
        {
            Resource? resource = ResourceLoader.LoadThreadedGet(path);
            AddToCache.Invoke(__instance, [resource, path]);
            finalized++;
        }

        return false;
    }
}
