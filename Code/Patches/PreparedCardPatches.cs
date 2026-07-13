using System.Reflection;
using System.Threading;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using NinjaSlayer.Code.Commands;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

internal static class PreparedRemovalContext
{
    private static readonly AsyncLocal<int> SuppressionDepth = new();

    public static bool IsSuppressed => SuppressionDepth.Value > 0;

    public static IDisposable Suppress()
    {
        SuppressionDepth.Value++;
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            SuppressionDepth.Value--;
        }
    }
}

public sealed class PreparedDrawPatch : IPatchMethod
{
    private static readonly AsyncLocal<int> BypassDepth = new();

    public static string PatchId => "ninjaslayer_prepared_draw_filter";

    public static string Description => "Keep prepared cards hidden from draws that do not satisfy Speedster timing.";

    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(CardPileCmd), nameof(CardPileCmd.Draw),
            [typeof(PlayerChoiceContext), typeof(decimal), typeof(Player), typeof(bool)])];

    public static bool Prefix(
        PlayerChoiceContext choiceContext,
        decimal count,
        Player player,
        bool fromHandDraw,
        ref Task<IEnumerable<CardModel>> __result)
    {
        CardPile drawPile = PileType.Draw.GetPile(player);
        if (BypassDepth.Value > 0
            || IsAllowedPreparedDraw(player, fromHandDraw)
            || !drawPile.Cards.Any(PrepareCmd.IsPrepared))
        {
            return true;
        }

        __result = DrawWithoutPrepared(choiceContext, count, player, fromHandDraw, drawPile);
        return false;
    }

    private static bool IsAllowedPreparedDraw(Player player, bool fromHandDraw)
    {
        return !fromHandDraw
            && player.Creature.CombatState?.CurrentSide == player.Creature.Side;
    }

    private static async Task<IEnumerable<CardModel>> DrawWithoutPrepared(
        PlayerChoiceContext choiceContext,
        decimal count,
        Player player,
        bool fromHandDraw,
        CardPile drawPile)
    {
        List<CardModel> preparedCards = drawPile.Cards.Where(PrepareCmd.IsPrepared).ToList();
        BypassDepth.Value++;
        using IDisposable removalSuppression = PreparedRemovalContext.Suppress();
        try
        {
            foreach (CardModel card in preparedCards)
            {
                if (drawPile.Cards.Contains(card))
                {
                    drawPile.RemoveInternal(card);
                }
            }

            return await CardPileCmd.Draw(choiceContext, count, player, fromHandDraw);
        }
        finally
        {
            try
            {
                for (int index = 0; index < preparedCards.Count; index++)
                {
                    CardModel card = preparedCards[index];
                    if (!drawPile.Cards.Contains(card) && !card.HasBeenRemovedFromState)
                    {
                        drawPile.AddInternal(card, Math.Min(index, drawPile.Cards.Count));
                    }
                }
            }
            finally
            {
                BypassDepth.Value--;
            }
        }
    }
}

public sealed class PreparedPileExitPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_prepared_pile_exit_cleanup";

    public static string Description => "Clear prepared when a card leaves the draw pile.";

    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(CardPile), nameof(CardPile.RemoveInternal), [typeof(CardModel), typeof(bool)])];

    public static void Prefix(CardPile __instance, CardModel card)
    {
        if (!PreparedRemovalContext.IsSuppressed
            && __instance.Type == PileType.Draw
            && PrepareCmd.IsPrepared(card))
        {
            CardCmd.ClearAffliction(card);
        }
    }
}

public sealed class PreparedDrawPileDisplayOrderPatch : IPatchMethod
{
    private static readonly FieldInfo? GridField =
        AccessTools.Field(typeof(NCardPileScreen), "_grid");

    public static string PatchId => "ninjaslayer_prepared_draw_pile_display_order";

    public static string Description =>
        "Show prepared cards first in draw order when viewing the draw pile.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NCardPileScreen), "OnPileContentsChanged")];

    public static void Postfix(NCardPileScreen __instance)
    {
        CardPile pile = __instance.Pile;
        if (pile.Type != PileType.Draw
            || !pile.Cards.Any(PrepareCmd.IsPrepared)
            || GridField?.GetValue(__instance) is not NCardGrid grid)
        {
            return;
        }

        List<CardModel> cards = pile.Cards
            .Where(PrepareCmd.IsPrepared)
            .Concat(pile.Cards
                .Where(card => !PrepareCmd.IsPrepared(card))
                .OrderBy(card => card.Rarity)
                .ThenBy(card => card.Id.Entry, StringComparer.Ordinal))
            .ToList();

        grid.SetCards(cards, PileType.Draw, [SortingOrders.Ascending]);
    }
}
