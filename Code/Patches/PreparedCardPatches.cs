using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Code.Prepared;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class PreparedDrawPatch : IPatchMethod
{
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
        if (IsAllowedPreparedDraw(player, fromHandDraw)
            || !drawPile.Cards.Any(PrepareCmd.IsPrepared))
        {
            return true;
        }

        __result = PreparedDrawService.Draw(choiceContext, count, player, fromHandDraw);
        return false;
    }

    private static bool IsAllowedPreparedDraw(Player player, bool fromHandDraw)
    {
        return !fromHandDraw
            && player.Creature.CombatState?.CurrentSide == player.Creature.Side;
    }

}

internal static class PreparedDrawService
{
    private static readonly MethodInfo ShuffleFtueCheck =
        AccessTools.Method(typeof(CardPileCmd), "ShuffleFtueCheck")
        ?? throw new MissingMethodException(typeof(CardPileCmd).FullName, "ShuffleFtueCheck");

    public static async Task<IEnumerable<CardModel>> Draw(
        PlayerChoiceContext choiceContext,
        decimal count,
        Player player,
        bool fromHandDraw)
    {
        if (CombatManager.Instance.IsOverOrEnding)
        {
            return [];
        }

        if (player.Creature.CombatState is not { } combatState)
        {
            return [];
        }

        bool drawAllowed = Hook.ShouldDraw(combatState, player, fromHandDraw, out AbstractModel? modifier);
        if (!drawAllowed)
        {
            if (modifier is not null)
            {
                await Hook.AfterPreventingDraw(combatState, modifier);
            }
            return [];
        }

        List<CardModel> result = [];
        CardPile hand = PileType.Hand.GetPile(player);
        CardPile drawPile = PileType.Draw.GetPile(player);
        int drawsRequested = count > 0m ? (int)Math.Ceiling(count) : 0;
        if (drawsRequested == 0)
        {
            return result;
        }

        int availableHandSlots = Math.Max(0, CardPile.MaxCardsInHand - hand.Cards.Count);
        if (availableHandSlots == 0)
        {
            CheckIfFilteredDrawIsPossible(player);
            return result;
        }

        for (int index = 0; index < drawsRequested; index++)
        {
            if (availableHandSlots <= 0 || CombatManager.Instance.IsOverOrEnding)
            {
                break;
            }

            if (!CheckIfFilteredDrawIsPossible(player))
            {
                break;
            }

            await ShuffleIfNecessary(choiceContext, player);
            if (!CheckIfFilteredDrawIsPossible(player))
            {
                break;
            }

            CardModel? card = drawPile.Cards.FirstOrDefault(card => !PrepareCmd.IsPrepared(card));
            if (card is null || hand.Cards.Count >= CardPile.MaxCardsInHand)
            {
                break;
            }

            result.Add(card);
            await CardPileCmd.Add(card, hand);
            CombatManager.Instance.History.CardDrawn(combatState, card, fromHandDraw);
            await Hook.AfterCardDrawn(combatState, choiceContext, card, fromHandDraw);
            card.InvokeDrawn();
            NDebugAudioManager.Instance?.Play("card_deal.mp3", 0.25f, PitchVariance.Small);
            availableHandSlots = Math.Max(0, CardPile.MaxCardsInHand - hand.Cards.Count);
        }

        return result;
    }

    private static bool CheckIfFilteredDrawIsPossible(Player player)
    {
        if (CombatManager.Instance.IsOverOrEnding)
        {
            return false;
        }

        if (PileType.Hand.GetPile(player).Cards.Count >= CardPile.MaxCardsInHand)
        {
            ThinkCmd.Play(new LocString("combat_messages", "HAND_FULL"), player.Creature, 2.0);
            return false;
        }

        if (PileType.Draw.GetPile(player).Cards.Any(card => !PrepareCmd.IsPrepared(card))
            || PileType.Discard.GetPile(player).Cards.Count > 0)
        {
            return true;
        }

        ThinkCmd.Play(new LocString("combat_messages", "NO_DRAW"), player.Creature, 2.0);
        return false;
    }

    private static async Task ShuffleIfNecessary(PlayerChoiceContext choiceContext, Player player)
    {
        CardPile drawPile = PileType.Draw.GetPile(player);
        CardPile discardPile = PileType.Discard.GetPile(player);
        if (CombatManager.Instance.IsOverOrEnding
            || PileType.Hand.GetPile(player).Cards.Count >= CardPile.MaxCardsInHand
            || drawPile.Cards.Any(card => !PrepareCmd.IsPrepared(card))
            || discardPile.Cards.Count == 0)
        {
            return;
        }

        Task shuffleFtue = ShuffleFtueCheck.Invoke(null, null) as Task
            ?? throw new InvalidOperationException("CardPileCmd.ShuffleFtueCheck did not return a Task.");
        await shuffleFtue;
        List<CardModel> shuffledCards = discardPile.Cards.ToList();
        shuffledCards.StableShuffle(player.RunState.Rng.Shuffle);
        if (player.Creature.CombatState is not { } combatState)
        {
            return;
        }

        Hook.ModifyShuffleOrder(combatState, player, shuffledCards, isInitialShuffle: false);

        float timeBetweenCardAdds = Math.Min(0.045f, 0.8f / shuffledCards.Count);
        float randomTimeBetweenCardAdds = 1.11f * timeBetweenCardAdds;
        float waitTimeAccumulator = 0f;
        foreach (CardModel card in shuffledCards)
        {
            await CardPileCmd.Add(card, drawPile);
            if (CombatManager.Instance.IsOverOrEnding)
            {
                return;
            }

            float wait = timeBetweenCardAdds
                + Rng.Chaotic.NextFloat(-randomTimeBetweenCardAdds * 0.5f, randomTimeBetweenCardAdds * 0.5f);
            waitTimeAccumulator += wait;
            if (waitTimeAccumulator >= ((SceneTree)Engine.GetMainLoop()).Root.GetProcessDeltaTime())
            {
                await Cmd.Wait(wait);
                waitTimeAccumulator = 0f;
            }
        }

        await Cmd.CustomScaledWait(0.2f, 0.5f);
        if (!CombatManager.Instance.IsOverOrEnding)
        {
            await Hook.AfterShuffle(combatState, choiceContext, player);
        }
    }

}

public sealed class PreparedDrawPileDisplayOrderPatch : IPatchMethod
{
    private static readonly FieldInfo? Grid =
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
        NCardGrid? grid = Grid?.GetValue(__instance) as NCardGrid;
        if (pile.Type != PileType.Draw
            || !pile.Cards.Any(PrepareCmd.IsPrepared)
            || grid is null)
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
