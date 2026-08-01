using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
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
                TransitionLoadConcurrencyPolicy.VanillaConcurrentLoadLimit),
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

        if (!GameCompatibility.AssetLoading.TryGetFinalizing(__instance, out Queue<string>? finalizing)
            || finalizing is null)
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

        NinjaSlayerTransitionLoadSmoothing.RecordFinalizeBatch(
            finalized,
            Stopwatch.GetElapsedTime(batchStartedAt));
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

public sealed class NinjaSlayerTransitionRunInitializationTracePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_transition_run_initialization_trace";

    public static string Description =>
        "Measure the major NRun ready and global-UI initialization phases during a NinjaSlayer transition.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NRun), nameof(NRun._Ready), Type.EmptyTypes),
        new(typeof(NGlobalUi), nameof(NGlobalUi._Ready), Type.EmptyTypes),
        new(typeof(NGlobalUi), nameof(NGlobalUi.Initialize), [typeof(RunState)]),
        new(typeof(NTopBar), nameof(NTopBar.Initialize), [typeof(IRunState)]),
        new(
            typeof(NMultiplayerPlayerStateContainer),
            nameof(NMultiplayerPlayerStateContainer.Initialize),
            [typeof(RunState)]),
        new(typeof(NRelicInventory), nameof(NRelicInventory.Initialize), [typeof(RunState)]),
        new(typeof(NMapScreen), nameof(NMapScreen.Initialize), [typeof(RunState)]),
        new(typeof(NRunMusicController), nameof(NRunMusicController.SetRunState), [typeof(IRunState)]),
        new(typeof(NRunMusicController), nameof(NRunMusicController.UpdateMusic), Type.EmptyTypes)
    ];

    // NRunMusicController.UpdateMusic and SetRunState are also called far outside transitions, and
    // RecordPhase discards the name when no session is armed. Building it unconditionally cost two
    // string allocations per call for a value that was thrown away.
    private static readonly Dictionary<MethodBase, string> PhaseNames = [];

    public static void Prefix(MethodBase __originalMethod, out TransitionPhasePatchState __state)
    {
        if (!NinjaSlayerTransitionLoadSmoothing.IsAnimationPlaying)
        {
            __state = default;
            return;
        }

        __state = new TransitionPhasePatchState(Stopwatch.GetTimestamp(), GetPhaseName(__originalMethod));
    }

    private static string GetPhaseName(MethodBase method)
    {
        lock (PhaseNames)
        {
            if (PhaseNames.TryGetValue(method, out string? name))
            {
                return name;
            }

            name = $"{method.DeclaringType?.Name ?? "unknown"}.{method.Name}";
            PhaseNames[method] = name;
            return name;
        }
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
