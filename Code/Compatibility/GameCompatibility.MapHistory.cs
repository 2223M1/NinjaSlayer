using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace NinjaSlayer.Code.Compatibility;

internal static partial class GameCompatibility
{
    internal static class MapHistory
    {
        private static readonly FieldInfo? RunState = AccessTools.Field(typeof(NMapPoint), "_runState");

        public static IReadOnlyList<CapabilityProbe> GetProbes() =>
        [
            CapabilityProbe.Optional(
                "NMapPoint.run-state",
                RunState != null,
                RunState != null ? "available" : "NMapPoint._runState is unavailable")
        ];

        public static bool TryGetRunState(NMapPoint point, out RunState? runState)
        {
            runState = RunState?.GetValue(point) as RunState;
            return runState is not null;
        }
    }
}
