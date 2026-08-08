using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using NinjaSlayer.Code.Transition;

namespace NinjaSlayer.Code.Compatibility;

internal static partial class GameCompatibility
{
    internal static class AssetLoading
    {
        private static readonly FieldInfo? Loading = AccessTools.Field(typeof(AssetLoadingSession), "_loading");
        private static readonly FieldInfo? Finalizing = AccessTools.Field(typeof(AssetLoadingSession), "_finalizing");
        private static readonly MethodInfo? AddToCache = AccessTools.Method(typeof(AssetLoadingSession), "AddToCache");
        public static MethodInfo? LoadingCount { get; } =
            AccessTools.PropertyGetter(typeof(Queue<string>), nameof(Queue<string>.Count));
        public static MethodInfo? FinalizeLoading { get; } =
            AccessTools.Method(typeof(AssetLoadingSession), "FinalizeLoading");
        public static MethodInfo? ProcessLoadingQueue { get; } =
            AccessTools.Method(typeof(AssetLoadingSession), "ProcessLoadingQueue");
        public static MethodInfo? ConcurrentAssetLoadLimit { get; } = AccessTools.Method(
            typeof(NinjaSlayerTransitionLoadSmoothing),
            nameof(NinjaSlayerTransitionLoadSmoothing.GetConcurrentAssetLoadLimit));
        public static MethodInfo? GcCollect { get; } = AccessTools.Method(typeof(GC), nameof(GC.Collect), Type.EmptyTypes);
        public static MethodInfo? SafeCollect { get; } = AccessTools.Method(
            typeof(NinjaSlayerTransitionLoadSmoothing),
            nameof(NinjaSlayerTransitionLoadSmoothing.CollectWhenSafe));
        public static MethodInfo? AncientInitializeVisuals { get; } =
            AccessTools.Method(typeof(MegaCrit.Sts2.Core.Nodes.Events.NAncientEventLayout), "InitializeVisuals");

        public static IReadOnlyList<CapabilityProbe> GetProbes()
        {
            bool stateMachinesAvailable = TryResolvePreloadStateMachines(out _, out string stateMachineReason);
            return
            [
                RequiredMember("AssetLoadingSession.loading", Loading, "AssetLoadingSession._loading"),
                RequiredMember("Queue.loading-count", LoadingCount, "Queue<string>.Count"),
                RequiredMember("AssetLoadingSession.finalizing", Finalizing, "AssetLoadingSession._finalizing"),
                RequiredMember("AssetLoadingSession.add-to-cache", AddToCache, "AssetLoadingSession.AddToCache"),
                RequiredMember(
                    "AssetLoadingSession.finalize-loading",
                    FinalizeLoading,
                    "AssetLoadingSession.FinalizeLoading"),
                RequiredMember(
                    "AssetLoadingSession.process-loading-queue",
                    ProcessLoadingQueue,
                    "AssetLoadingSession.ProcessLoadingQueue"),
                RequiredMember(
                    "TransitionLoadSmoothing.concurrent-load-limit",
                    ConcurrentAssetLoadLimit,
                    "NinjaSlayerTransitionLoadSmoothing.GetConcurrentAssetLoadLimit"),
                RequiredMember("GC.collect", GcCollect, "System.GC.Collect()"),
                RequiredMember(
                    "TransitionLoadSmoothing.safe-collect",
                    SafeCollect,
                    "NinjaSlayerTransitionLoadSmoothing.CollectWhenSafe"),
                CapabilityProbe.Required(
                    "PreloadManager.state-machines",
                    stateMachinesAvailable,
                    stateMachinesAvailable ? "validated" : stateMachineReason),
                CapabilityProbe.Optional(
                    "NAncientEventLayout.initialize-visuals",
                    AncientInitializeVisuals != null,
                    AncientInitializeVisuals != null
                        ? "available"
                        : "NAncientEventLayout.InitializeVisuals is unavailable")
            ];
        }

        public static bool TryGetFinalizing(
            AssetLoadingSession session,
            [NotNullWhen(true)] out Queue<string>? finalizing)
        {
            finalizing = Finalizing?.GetValue(session) as Queue<string>;
            return finalizing != null;
        }

        public static void Cache(AssetLoadingSession session, Resource? resource, string path) =>
            AddToCache?.Invoke(session, [resource, path]);

        public static bool IsLoadingCountLimitSite(
            IReadOnlyList<CodeInstruction> code,
            int index,
            int expectedLimit)
        {
            return Loading is not null
                && LoadingCount is not null
                && index >= 3
                && code[index].LoadsConstant(expectedLimit)
                && HarmonyLib.CodeInstructionExtensions.IsLdarg(code[index - 3], 0)
                && code[index - 2].LoadsField(Loading)
                && code[index - 1].Calls(LoadingCount);
        }

        public static bool TryResolvePreloadStateMachines(
            out RuntimePatchTarget[] targets,
            out string missingMember)
        {
            var signatures = new (string IdSuffix, string Name, Type[] Parameters)[]
            {
                ("load-run-assets", nameof(PreloadManager.LoadRunAssets), [typeof(IEnumerable<CharacterModel>)]),
                ("load-act-assets", nameof(PreloadManager.LoadActAssets), [typeof(ActModel)]),
                ("load-room-assets", "LoadRoomAssets", [typeof(string), typeof(IEnumerable<string>)])
            };
            var resolved = new List<RuntimePatchTarget>(signatures.Length);
            foreach ((string idSuffix, string methodName, Type[] parameterTypes) in signatures)
            {
                MethodInfo? method = AccessTools.Method(typeof(PreloadManager), methodName, parameterTypes);
                Type? stateMachine = method?.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
                MethodInfo? moveNext = stateMachine is null
                    ? null
                    : AccessTools.Method(stateMachine, "MoveNext", Type.EmptyTypes);
                if (moveNext == null)
                {
                    targets = [];
                    missingMember = $"{typeof(PreloadManager).FullName}.{methodName} async state machine";
                    return false;
                }

                resolved.Add(new RuntimePatchTarget(idSuffix, moveNext));
            }

            targets = resolved.ToArray();
            missingMember = string.Empty;
            return true;
        }
    }
}
