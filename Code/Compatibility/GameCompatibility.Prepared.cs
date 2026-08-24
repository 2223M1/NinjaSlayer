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
        private static readonly MethodInfo ShuffleFtueCheck =
            AccessTools.Method(typeof(CardPileCmd), "ShuffleFtueCheck")
            ?? throw new MissingMethodException(typeof(CardPileCmd).FullName, "ShuffleFtueCheck");
        private static readonly FieldInfo? Grid = AccessTools.Field(typeof(NCardPileScreen), "_grid");

        public static async Task ShowShuffleFtue()
        {
            Task task = ShuffleFtueCheck.Invoke(null, null) as Task
                ?? throw new InvalidOperationException("CardPileCmd.ShuffleFtueCheck did not return a Task.");
            await task;
        }

        public static bool TryGetGrid(NCardPileScreen screen, out NCardGrid? grid)
        {
            grid = Grid?.GetValue(screen) as NCardGrid;
            return grid != null;
        }

    }
}
