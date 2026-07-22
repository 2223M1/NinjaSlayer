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

    public static bool IsCritical => true;

    internal static bool CanInstall(out string missingMember)
    {
        if (FinalizingField == null)
        {
            missingMember = $"{typeof(AssetLoadingSession).FullName}._finalizing";
            return false;
        }

        if (AddToCacheMethod == null)
        {
            missingMember = $"{typeof(AssetLoadingSession).FullName}.AddToCache";
            return false;
        }

        missingMember = string.Empty;
        return true;
    }

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
    private static readonly MethodInfo? GcCollectMethod =
        AccessTools.Method(typeof(GC), nameof(GC.Collect), Type.EmptyTypes);

    private static readonly MethodInfo? SafeCollectMethod =
        AccessTools.Method(
            typeof(NinjaSlayerTransitionLoadSmoothing),
            nameof(NinjaSlayerTransitionLoadSmoothing.CollectWhenSafe));

    public static string PatchId => "ninjaslayer_transition_preload_gc_deferral";

    public static string Description =>
        "Defer forced run-asset garbage collection until the NinjaSlayer transition is covered by black.";

    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() => TryResolveTargets(out ModPatchTarget[] targets, out _)
        ? targets
        : [];

    internal static bool CanInstall(out string missingMember)
    {
        if (GcCollectMethod == null)
        {
            missingMember = $"{typeof(GC).FullName}.{nameof(GC.Collect)}";
            return false;
        }

        if (SafeCollectMethod == null)
        {
            missingMember = $"{typeof(NinjaSlayerTransitionLoadSmoothing).FullName}.CollectWhenSafe";
            return false;
        }

        return TryResolveTargets(out _, out missingMember);
    }

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (CodeInstruction instruction in instructions)
        {
            if (GcCollectMethod != null && SafeCollectMethod != null && instruction.Calls(GcCollectMethod))
            {
                instruction.operand = SafeCollectMethod;
            }

            yield return instruction;
        }
    }

    private static bool TryResolveTargets(out ModPatchTarget[] targets, out string missingMember)
    {
        var signatures = new (string Name, Type[] Parameters)[]
        {
            (nameof(PreloadManager.LoadRunAssets), [typeof(IEnumerable<CharacterModel>)]),
            (nameof(PreloadManager.LoadActAssets), [typeof(ActModel)]),
            ("LoadRoomAssets", [typeof(string), typeof(IEnumerable<string>)])
        };
        var resolved = new List<ModPatchTarget>(signatures.Length);
        foreach ((string methodName, Type[] parameterTypes) in signatures)
        {
            MethodInfo? method = AccessTools.Method(typeof(PreloadManager), methodName, parameterTypes);
            Type? stateMachineType = method?.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
            if (stateMachineType == null)
            {
                targets = [];
                missingMember = $"{typeof(PreloadManager).FullName}.{methodName} async state machine";
                return false;
            }

            resolved.Add(new ModPatchTarget(stateMachineType, nameof(IAsyncStateMachine.MoveNext)));
        }

        targets = resolved.ToArray();
        missingMember = string.Empty;
        return true;
    }
}
