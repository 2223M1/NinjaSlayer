using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.FeedbackScreen;

namespace NinjaSlayer.Code.Compatibility;

internal static partial class GameCompatibility
{
    internal static class Feedback
    {
        private static readonly MethodInfo SendButtonSelected =
            AccessTools.Method(typeof(NSendFeedbackScreen), "SendButtonSelected", [typeof(NButton)])
            ?? throw new MissingMethodException(
                typeof(NSendFeedbackScreen).FullName,
                "SendButtonSelected");

        public static void SelectSendButton(NSendFeedbackScreen screen, NButton button)
        {
            SendButtonSelected.Invoke(screen, [button]);
        }
    }
}
