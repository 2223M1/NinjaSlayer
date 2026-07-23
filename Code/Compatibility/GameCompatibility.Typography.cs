using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.InspectScreens;

namespace NinjaSlayer.Code.Compatibility;

internal static partial class GameCompatibility
{
    internal static class Typography
    {
        private static readonly FieldInfo? Relics = AccessTools.Field(typeof(NInspectRelicScreen), "_relics");
        private static readonly FieldInfo? Index = AccessTools.Field(typeof(NInspectRelicScreen), "_index");

        public static IReadOnlyList<CapabilityProbe> GetProbes() =>
        [
            CapabilityProbe.Optional(
                "NInspectRelicScreen.relics",
                Relics != null,
                Relics != null ? "available" : "NInspectRelicScreen._relics is unavailable"),
            CapabilityProbe.Optional(
                "NInspectRelicScreen.index",
                Index != null,
                Index != null ? "available" : "NInspectRelicScreen._index is unavailable")
        ];

        public static bool TryGetSelectedRelic(NInspectRelicScreen screen, out RelicModel? relic)
        {
            relic = null;
            if (Relics?.GetValue(screen) is not IReadOnlyList<RelicModel> relics
                || Index?.GetValue(screen) is not int index
                || index < 0
                || index >= relics.Count)
            {
                return false;
            }

            relic = relics[index];
            return true;
        }
    }
}
