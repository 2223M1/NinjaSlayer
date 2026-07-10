using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Code.Transition;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class NinjaSlayerTransitionAssetFinalizePatch : IPatchMethod
{
    private static readonly FieldInfo? FinalizingField =
        AccessTools.Field(typeof(AssetLoadingSession), "_finalizing");

    private static readonly MethodInfo? AddToCacheMethod =
        AccessTools.Method(typeof(AssetLoadingSession), "AddToCache");

    public static string PatchId => "ninjaslayer_transition_asset_finalize_batching";

    public static string Description =>
        "Finalize threaded resources in small batches while the NinjaSlayer transition is visible.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(AssetLoadingSession), "FinalizeLoading")];

    public static bool Prefix(AssetLoadingSession __instance)
    {
        if (!NinjaSlayerTransitionLoadSmoothing.IsAnimationPlaying)
        {
            return true;
        }

        if (FinalizingField?.GetValue(__instance) is not Queue<string> finalizing ||
            AddToCacheMethod == null)
        {
            return true;
        }

        long batchStartedAt = Stopwatch.GetTimestamp();
        var finalized = 0;
        while (finalized < NinjaSlayerTransitionLoadSmoothing.FinalizeBatchSize &&
               finalizing.TryDequeue(out string? path))
        {
            Resource? resource = ResourceLoader.LoadThreadedGet(path);
            AddToCacheMethod.Invoke(__instance, [resource, path]);
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
    private static readonly MethodInfo GcCollectMethod =
        AccessTools.Method(typeof(GC), nameof(GC.Collect), Type.EmptyTypes);

    private static readonly MethodInfo SafeCollectMethod =
        AccessTools.Method(
            typeof(NinjaSlayerTransitionLoadSmoothing),
            nameof(NinjaSlayerTransitionLoadSmoothing.CollectWhenSafe));

    public static string PatchId => "ninjaslayer_transition_preload_gc_deferral";

    public static string Description =>
        "Defer forced run-asset garbage collection until the NinjaSlayer transition is covered by black.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(GetAsyncStateMachineType(nameof(PreloadManager.LoadRunAssets), [typeof(IEnumerable<CharacterModel>)]),
            nameof(IAsyncStateMachine.MoveNext)),
        new(GetAsyncStateMachineType(nameof(PreloadManager.LoadActAssets), [typeof(ActModel)]),
            nameof(IAsyncStateMachine.MoveNext)),
        new(GetAsyncStateMachineType("LoadRoomAssets", [typeof(string), typeof(IEnumerable<string>)]),
            nameof(IAsyncStateMachine.MoveNext))
    ];

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(GcCollectMethod))
            {
                instruction.operand = SafeCollectMethod;
            }

            yield return instruction;
        }
    }

    private static Type GetAsyncStateMachineType(string methodName, Type[] parameterTypes)
    {
        MethodInfo method = AccessTools.Method(typeof(PreloadManager), methodName, parameterTypes)
            ?? throw new MissingMethodException(typeof(PreloadManager).FullName, methodName);

        return method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
            ?? throw new InvalidOperationException($"{method.DeclaringType?.FullName}.{method.Name} is not async.");
    }
}
