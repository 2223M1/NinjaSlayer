using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Saves;
using NinjaSlayer.Code.Transition;

namespace NinjaSlayer.Code.Compatibility;

internal static partial class GameCompatibility
{
    internal static class AssetLoading
    {
        private static readonly FieldInfo? Finalizing = AccessTools.Field(typeof(AssetLoadingSession), "_finalizing");
        private static readonly MethodInfo? AddToCache = AccessTools.Method(typeof(AssetLoadingSession), "AddToCache");
        private static readonly MethodInfo? GetRunAssetPaths = AccessTools.Method(
            typeof(PreloadManager),
            "GetRunAssetPaths",
            [typeof(IEnumerable<CharacterModel>), typeof(bool)]);
        private static readonly FieldInfo? PendingRunSave = AccessTools.Field(
            typeof(NMainMenu),
            "_readRunSaveResult");
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

        public static IReadOnlyList<CapabilityProbe> GetPrefetchProbes() =>
        [
            RequiredMember(
                "PreloadManager.get-run-asset-paths",
                GetRunAssetPaths,
                "PreloadManager.GetRunAssetPaths(IEnumerable<CharacterModel>, bool)"),
            CapabilityProbe.Optional(
                "NMainMenu.pending-run-save",
                PendingRunSave != null,
                PendingRunSave != null ? "available" : "NMainMenu._readRunSaveResult is unavailable")
        ];

        public static bool TryGetFinalizing(AssetLoadingSession session, out Queue<string>? finalizing)
        {
            finalizing = Finalizing?.GetValue(session) as Queue<string>;
            return finalizing != null;
        }

        public static void Cache(AssetLoadingSession session, Resource? resource, string path) =>
            AddToCache?.Invoke(session, [resource, path]);

        public static bool TryGetRunAssetPaths(
            IEnumerable<CharacterModel> characters,
            bool isMultiplayer,
            out IReadOnlyList<string> paths)
        {
            if (GetRunAssetPaths?.Invoke(null, [characters, isMultiplayer]) is not IEnumerable<string> result)
            {
                paths = [];
                return false;
            }

            paths = result
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return true;
        }

        public static bool TryGetPendingRunSave(NMainMenu mainMenu, out SerializableRun? save)
        {
            save = (PendingRunSave?.GetValue(mainMenu) as ReadSaveResult<SerializableRun>)?.SaveData;
            return save is not null;
        }

        public static bool TryResolvePreloadStateMachines(out Type[] stateMachines, out string missingMember)
        {
            var signatures = new (string Name, Type[] Parameters)[]
            {
                (nameof(PreloadManager.LoadRunAssets), [typeof(IEnumerable<CharacterModel>)]),
                (nameof(PreloadManager.LoadActAssets), [typeof(ActModel)]),
                ("LoadRoomAssets", [typeof(string), typeof(IEnumerable<string>)])
            };
            var resolved = new List<Type>(signatures.Length);
            foreach ((string methodName, Type[] parameterTypes) in signatures)
            {
                MethodInfo? method = AccessTools.Method(typeof(PreloadManager), methodName, parameterTypes);
                Type? stateMachine = method?.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
                if (stateMachine == null)
                {
                    stateMachines = [];
                    missingMember = $"{typeof(PreloadManager).FullName}.{methodName} async state machine";
                    return false;
                }

                resolved.Add(stateMachine);
            }

            stateMachines = resolved.ToArray();
            missingMember = string.Empty;
            return true;
        }
    }
}
