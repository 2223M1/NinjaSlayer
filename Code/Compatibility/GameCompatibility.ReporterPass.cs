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
        private static readonly MethodInfo SetEventFinished =
            AccessTools.Method(typeof(EventModel), "SetEventFinished", [typeof(LocString)])
            ?? throw new MissingMethodException(typeof(EventModel).FullName, "SetEventFinished");

        public static void Finish(EventModel eventModel, LocString result)
        {
            SetEventFinished.Invoke(eventModel, [result]);
        }
    }
}
