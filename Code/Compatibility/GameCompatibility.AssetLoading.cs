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
        private static readonly FieldInfo? Finalizing = AccessTools.Field(typeof(AssetLoadingSession), "_finalizing");
        private static readonly MethodInfo? AddToCache = AccessTools.Method(typeof(AssetLoadingSession), "AddToCache");
        public static MethodInfo? GcCollect { get; } = AccessTools.Method(typeof(GC), nameof(GC.Collect), Type.EmptyTypes);
        public static MethodInfo? SafeCollect { get; } = AccessTools.Method(
            typeof(NinjaSlayerTransitionLoadSmoothing),
            nameof(NinjaSlayerTransitionLoadSmoothing.CollectWhenSafe));

        public static IReadOnlyList<CapabilityProbe> GetProbes()
        {
            bool stateMachinesAvailable = TryResolvePreloadStateMachines(out _, out string stateMachineReason);
            return
            [
                RequiredMember("AssetLoadingSession.finalizing", Finalizing, "AssetLoadingSession._finalizing"),
                RequiredMember("AssetLoadingSession.add-to-cache", AddToCache, "AssetLoadingSession.AddToCache"),
                RequiredMember("GC.collect", GcCollect, "System.GC.Collect()"),
                RequiredMember(
                    "TransitionLoadSmoothing.safe-collect",
                    SafeCollect,
                    "NinjaSlayerTransitionLoadSmoothing.CollectWhenSafe"),
                CapabilityProbe.Required(
                    "PreloadManager.state-machines",
                    stateMachinesAvailable,
                    stateMachinesAvailable ? "validated" : stateMachineReason)
            ];
        }

        public static bool TryGetFinalizing(AssetLoadingSession session, out Queue<string>? finalizing)
        {
            finalizing = Finalizing?.GetValue(session) as Queue<string>;
            return finalizing != null;
        }

        public static void Cache(AssetLoadingSession session, Resource? resource, string path) =>
            AddToCache?.Invoke(session, [resource, path]);

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
