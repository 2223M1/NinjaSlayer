using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace NinjaSlayer.Code.Compatibility;

internal static partial class GameCompatibility
{
    internal static class Prepared
    {
        private static readonly MethodInfo? ShuffleFtueCheck = AccessTools.Method(typeof(CardPileCmd), "ShuffleFtueCheck");
        private static readonly MethodInfo? AfterCardChangedPiles = AccessTools.Method(
            typeof(Hook),
            nameof(Hook.AfterCardChangedPiles),
            [typeof(IRunState), typeof(MegaCrit.Sts2.Core.Combat.ICombatState), typeof(CardModel), typeof(PileType), typeof(AbstractModel)]);
        private static readonly MethodInfo? BeforeCombatStart = AccessTools.Method(
            typeof(Hook),
            nameof(Hook.BeforeCombatStart),
            [typeof(IRunState), typeof(MegaCrit.Sts2.Core.Combat.ICombatState)]);
        private static readonly MethodInfo? InitializeSavedRun = AccessTools.Method(
            typeof(RunManager),
            "InitializeSavedRun",
            [typeof(SerializableRun)]);
        private static readonly PropertyInfo? RunManagerState = AccessTools.Property(typeof(RunManager), "State");
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
                    "CardPileCmd.draw-wrapper-and-internal-contract",
                    drawContractMatches,
                    drawContractMatches ? fingerprint.ToString() : reason),
                CapabilityProbe.Required(
                    "CardPile.prepared-queue-contract",
                    queueContractMatches,
                    queueContractMatches ? queueFingerprint.ToString() : queueReason),
                RequiredMember("CardPileCmd.shuffle-ftue", ShuffleFtueCheck, "CardPileCmd.ShuffleFtueCheck()")
            ];
        }

        public static IReadOnlyList<CapabilityProbe> GetSafetyProbes() =>
        [
            RequiredMember(
                "Hook.after-card-changed-piles",
                AfterCardChangedPiles,
                "Hook.AfterCardChangedPiles(IRunState, ICombatState, CardModel, PileType, AbstractModel)"),
            RequiredMember(
                "Hook.before-combat-start",
                BeforeCombatStart,
                "Hook.BeforeCombatStart(IRunState, ICombatState)"),
            CapabilityProbe.Optional(
                "RunManager.initialize-saved-run",
                InitializeSavedRun != null && RunManagerState != null,
                InitializeSavedRun != null && RunManagerState != null
                    ? "available"
                    : "RunManager.InitializeSavedRun(SerializableRun) or RunManager.State is unavailable")
        ];

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

        public static bool TryGetRunState(RunManager manager, out IRunState? runState)
        {
            runState = RunManagerState?.GetValue(manager) as IRunState;
            return runState is not null;
        }
    }
}
