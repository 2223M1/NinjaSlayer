using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Code.Commands;

public static class NinjaSlayerCardCmd
{

    public static async Task<int> ChooseAndDiscard(
        PlayerChoiceContext choiceContext,
        Player owner,
        int count,
        CardModel source)
    {
        int available = PileType.Hand.GetPile(owner).Cards.Count;
        int selectionCount = Math.Min(Math.Max(0, count), available);
        if (selectionCount == 0)
        {
            return 0;
        }

        List<CardModel> selected = (await CardSelectCmd.FromHandForDiscard(
            choiceContext,
            owner,
            new CardSelectorPrefs(CardSelectorPrefs.DiscardSelectionPrompt, selectionCount),
            null,
            source)).ToList();
        foreach (CardModel card in selected)
        {
            await CardCmd.Discard(choiceContext, card);
        }

        return selected.Count;
    }

    public static async Task<bool> ChooseAndExhaustRedesignChado(
        PlayerChoiceContext choiceContext,
        Player owner,
        CardModel source)
    {
        CardModel? selected = (await CardSelectCmd.FromHand(
            choiceContext,
            owner,
            new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt, 1),
            card => card is ChadoEnergyRedesignV1,
            source)).FirstOrDefault();
        if (selected is null)
        {
            return false;
        }

        await CardCmd.Exhaust(choiceContext, selected);
        return true;
    }

    public static async Task AddGeneratedCard<T>(Player owner, PileType pile, CardPilePosition position = CardPilePosition.Bottom)
        where T : CardModel
    {
        ICombatState combatState = owner.Creature.CombatState ?? throw new InvalidOperationException("Generated cards require an active combat state.");
        CardPileAddResult result = await CardPileCmd.AddGeneratedCardToCombat(combatState.CreateCard<T>(owner), pile, owner, position);
        if (pile is PileType.Draw or PileType.Discard)
        {
            CardCmd.PreviewCardPileAdd(result);
        }
    }
}
