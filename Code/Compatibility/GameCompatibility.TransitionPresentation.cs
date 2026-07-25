using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Events;

namespace NinjaSlayer.Code.Compatibility;

internal static partial class GameCompatibility
{
    internal static class TransitionPresentation
    {
        public static MethodInfo? AncientHealVfx { get; } = AccessTools.Method(
            typeof(NAncientEventLayout),
            "PlayHealVfxAfterFadeIn",
            [typeof(Player), typeof(decimal)]);

        public static IReadOnlyList<CapabilityProbe> GetProbes() =>
        [
            RequiredMember(
                "NAncientEventLayout.heal-vfx",
                AncientHealVfx,
                "NAncientEventLayout.PlayHealVfxAfterFadeIn")
        ];
    }
}
