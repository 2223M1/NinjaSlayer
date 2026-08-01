using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Models;

namespace NinjaSlayer.Code.Compatibility;

internal static partial class GameCompatibility
{
    internal static class Prepared
    {
        private static readonly MethodInfo? ShuffleFtueCheck = AccessTools.Method(typeof(CardPileCmd), "ShuffleFtueCheck");
        private static readonly FieldInfo? Grid = AccessTools.Field(typeof(NCardPileScreen), "_grid");

        public static IReadOnlyList<CapabilityProbe> GetGameplayProbes()
        {
            bool drawContractMatches = PreparedDrawTargetContract.TryValidate(
                out _,
                out PreparedDrawTargetFingerprint fingerprint,
                out string reason);
            bool queueContractMatches = PreparedQueueCompatibility.TryValidate(
                out PreparedQueueFingerprint queueFingerprint,
                out string queueReason);
            return
            [
                CapabilityProbe.Required(
                    "CardPileCmd.draw-host-contract",
                    drawContractMatches,
                    drawContractMatches ? fingerprint.ToString() : reason),
                CapabilityProbe.Required(
                    "CardPile.prepared-queue-contract",
                    queueContractMatches,
                    queueContractMatches ? queueFingerprint.ToString() : queueReason),
                RequiredMember("CardPileCmd.shuffle-ftue", ShuffleFtueCheck, "CardPileCmd.ShuffleFtueCheck()")
            ];
        }

        public static IReadOnlyList<CapabilityProbe> GetUiProbes() =>
        [
            RequiredMember("NCardPileScreen.grid", Grid, "NCardPileScreen._grid")
        ];

        public static async Task ShowShuffleFtue()
        {
            if (ShuffleFtueCheck?.Invoke(null, null) is Task task)
            {
                await task;
            }
        }

        public static bool TryGetGrid(NCardPileScreen screen, out NCardGrid? grid)
        {
            grid = Grid?.GetValue(screen) as NCardGrid;
            return grid != null;
        }

    }
}
