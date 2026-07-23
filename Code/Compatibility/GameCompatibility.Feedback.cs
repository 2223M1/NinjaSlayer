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
        private static readonly MethodInfo? SendButtonSelected =
            AccessTools.Method(typeof(NSendFeedbackScreen), "SendButtonSelected", [typeof(NButton)]);

        public static IReadOnlyList<CapabilityProbe> GetProbes() =>
        [
            RequiredMember("NSendFeedbackScreen.send-button-selected", SendButtonSelected,
                "NSendFeedbackScreen.SendButtonSelected(NButton)")
        ];

        public static bool TrySelectSendButton(NSendFeedbackScreen screen, NButton button)
        {
            if (SendButtonSelected == null)
            {
                return false;
            }

            SendButtonSelected.Invoke(screen, [button]);
            return true;
        }
    }
}
