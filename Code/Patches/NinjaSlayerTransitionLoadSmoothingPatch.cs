using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
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

public static class NinjaSlayerTransitionGcDeferralPatch
{
    private const string PatchIdPrefix = "ninjaslayer_transition_preload_gc_deferral";
    private static readonly MethodInfo LoadRunAssetsMoveNext = ResolveMoveNext(
        nameof(PreloadManager.LoadRunAssets),
        [typeof(IEnumerable<CharacterModel>)]);
    private static readonly MethodInfo LoadActAssetsMoveNext = ResolveMoveNext(
        nameof(PreloadManager.LoadActAssets),
        [typeof(ActModel)]);
    private static readonly MethodInfo LoadRoomAssetsMoveNext = ResolveMoveNext(
        "LoadRoomAssets",
        [typeof(string), typeof(IEnumerable<string>)]);
    private static readonly MethodInfo GcCollect =
        AccessTools.Method(typeof(GC), nameof(GC.Collect), Type.EmptyTypes)
        ?? throw new MissingMethodException(typeof(GC).FullName, nameof(GC.Collect));
    private static readonly MethodInfo SafeCollect = AccessTools.Method(
        typeof(NinjaSlayerTransitionLoadSmoothing),
        nameof(NinjaSlayerTransitionLoadSmoothing.CollectWhenSafe))
        ?? throw new MissingMethodException(
            typeof(NinjaSlayerTransitionLoadSmoothing).FullName,
            nameof(NinjaSlayerTransitionLoadSmoothing.CollectWhenSafe));

    public static DynamicPatchInfo[] CreateDynamicPatches()
    {
        var harmonyTranspiler = new HarmonyMethod(
            typeof(NinjaSlayerTransitionGcDeferralPatch),
            nameof(Transpiler));
        return
        [
            CreateDynamicPatch("load-run-assets", LoadRunAssetsMoveNext, harmonyTranspiler),
            CreateDynamicPatch("load-act-assets", LoadActAssetsMoveNext, harmonyTranspiler),
            CreateDynamicPatch("load-room-assets", LoadRoomAssetsMoveNext, harmonyTranspiler)
        ];
    }

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        const string operation = "NinjaSlayer transition GC deferral";
        var rewriter = HarmonyIlRewriter.From(instructions);
        HarmonyIlRewriteReport report = rewriter.RedirectCalls(
            operation,
            called => called == GcCollect ? SafeCollect : null,
            code => code.Any(HarmonyIl.IsCall(SafeCollect)));
        return rewriter.InstructionsChecked(report);
    }

    private static DynamicPatchInfo CreateDynamicPatch(
        string idSuffix,
        MethodInfo moveNext,
        HarmonyMethod transpiler) =>
        new(
            $"{PatchIdPrefix}_{idSuffix}",
            moveNext,
            transpiler: transpiler,
            isCritical: true,
            description: $"Defer forced GC in PreloadManager {idSuffix}.");

    private static MethodInfo ResolveMoveNext(string methodName, Type[] parameterTypes)
    {
        MethodInfo method = AccessTools.Method(typeof(PreloadManager), methodName, parameterTypes)
            ?? throw new MissingMethodException(typeof(PreloadManager).FullName, methodName);
        Type stateMachine = method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
            ?? throw new MissingMemberException(
                method.DeclaringType?.FullName,
                $"{method.Name} async state machine");
        return AccessTools.Method(stateMachine, "MoveNext", Type.EmptyTypes)
            ?? throw new MissingMethodException(stateMachine.FullName, "MoveNext");
    }
}
