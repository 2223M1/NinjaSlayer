using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Code.Transition;
using STS2RitsuLib.Patching.Models;

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
        List<CodeInstruction> rewritten = instructions.ToList();
        MethodInfo replacement = GameCompatibility.AssetLoading.ConcurrentAssetLoadLimit
            ?? throw new MissingMethodException(
                typeof(NinjaSlayerTransitionLoadSmoothing).FullName,
                nameof(NinjaSlayerTransitionLoadSmoothing.GetConcurrentAssetLoadLimit));

        var replacements = 0;
        foreach (CodeInstruction instruction in rewritten)
        {
            if (!instruction.LoadsConstant(TransitionLoadConcurrencyPolicy.VanillaConcurrentLoadLimit))
            {
                continue;
            }

            instruction.opcode = OpCodes.Call;
            instruction.operand = replacement;
            replacements++;
        }

        if (replacements != 1)
        {
            throw new InvalidOperationException(
                $"Expected one vanilla asset concurrency limit, found {replacements}.");
        }

        return rewritten;
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

public readonly record struct TransitionPhasePatchState(long StartedAt, string? Name);

public sealed class NinjaSlayerTransitionRunSceneTracePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_transition_run_scene_trace";

    public static string Description =>
        "Measure cold NRun instantiation while a NinjaSlayer transition is active.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NRun), nameof(NRun.Create), [typeof(RunState)])];

    public static void Prefix(out TransitionPhasePatchState __state) =>
        __state = new TransitionPhasePatchState(Stopwatch.GetTimestamp(), "nrun_instantiate");

    public static void Postfix(TransitionPhasePatchState __state) => Record(__state);

    private static void Record(TransitionPhasePatchState state)
    {
        if (state.Name is not null)
        {
            NinjaSlayerTransitionLoadSmoothing.RecordPhase(
                state.Name,
                Stopwatch.GetElapsedTime(state.StartedAt));
        }
    }
}

public sealed class NinjaSlayerTransitionSceneTreeTracePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_transition_scene_tree_trace";

    public static string Description =>
        "Measure NRun and event-room tree entry while a NinjaSlayer transition is active.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NSceneContainer), nameof(NSceneContainer.SetCurrentScene), [typeof(Control)])];

    public static void Prefix(Control node, out TransitionPhasePatchState __state)
    {
        string? name = node switch
        {
            NRun => "nrun_enter_tree",
            NEventRoom => "event_room_enter_tree",
            _ => null
        };
        __state = new TransitionPhasePatchState(Stopwatch.GetTimestamp(), name);
    }

    public static void Postfix(TransitionPhasePatchState __state)
    {
        if (__state.Name is not null)
        {
            NinjaSlayerTransitionLoadSmoothing.RecordPhase(
                __state.Name,
                Stopwatch.GetElapsedTime(__state.StartedAt));
        }
    }
}

public sealed class NinjaSlayerTransitionEventSceneTracePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_transition_event_scene_trace";

    public static string Description =>
        "Measure event-room instantiation and Ancient visual initialization during a NinjaSlayer transition.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets()
    {
        var targets = new List<ModPatchTarget>
        {
            new(
            typeof(NEventRoom),
            nameof(NEventRoom.Create),
            [typeof(EventModel), typeof(IRunState), typeof(bool)])
        };
        if (GameCompatibility.AssetLoading.AncientInitializeVisuals is { } ancientTarget)
        {
            targets.Add(new ModPatchTarget(ancientTarget.DeclaringType!, ancientTarget.Name));
        }

        return [.. targets];
    }

    public static void Prefix(System.Reflection.MethodBase __originalMethod, out TransitionPhasePatchState __state)
    {
        string name = __originalMethod.DeclaringType == typeof(NEventRoom)
            ? "event_room_instantiate"
            : "ancient_visuals";
        __state = new TransitionPhasePatchState(Stopwatch.GetTimestamp(), name);
    }

    public static void Postfix(TransitionPhasePatchState __state) =>
        NinjaSlayerTransitionLoadSmoothing.RecordPhase(
            __state.Name ?? "event_scene",
            Stopwatch.GetElapsedTime(__state.StartedAt));
}
