using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models.Events;

namespace NinjaSlayer.Code.Compatibility;

internal static partial class GameCompatibility
{
    internal static class OrobasSeaGlass
    {
        public const string TargetMethodName = "GenerateInitialOptions";

        private static readonly MethodInfo? GenerateInitialOptions = AccessTools.Method(
            typeof(Orobas),
            TargetMethodName,
            Type.EmptyTypes);

        public static IReadOnlyList<CapabilityProbe> GetProbes() =>
        [
            CapabilityProbe.Required(
                "Orobas.generate-initial-options",
                GenerateInitialOptions?.ReturnType == typeof(IReadOnlyList<EventOption>),
                GenerateInitialOptions == null
                    ? "Orobas.GenerateInitialOptions() is unavailable"
                    : GenerateInitialOptions.ReturnType == typeof(IReadOnlyList<EventOption>)
                        ? "available"
                        : $"expected IReadOnlyList<EventOption>, found {GenerateInitialOptions.ReturnType}")
        ];
    }
}
