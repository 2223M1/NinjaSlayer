using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace NinjaSlayer.Code.Compatibility;

internal static partial class GameCompatibility
{
    internal static class ReporterPass
    {
        private static readonly MethodInfo? SetEventFinished =
            AccessTools.Method(typeof(EventModel), "SetEventFinished", [typeof(LocString)]);

        public static IReadOnlyList<CapabilityProbe> GetProbes() =>
        [
            RequiredMember("EventModel.set-event-finished", SetEventFinished,
                "EventModel.SetEventFinished(LocString)")
        ];

        public static bool TryFinish(EventModel eventModel, LocString result)
        {
            if (SetEventFinished == null)
            {
                return false;
            }

            SetEventFinished.Invoke(eventModel, [result]);
            return true;
        }
    }
}
