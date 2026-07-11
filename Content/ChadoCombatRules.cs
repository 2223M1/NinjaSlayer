using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using NinjaSlayer.Cards;
using STS2RitsuLib.Interop.AutoRegistration;

namespace NinjaSlayer.Content;

[RegisterSingleton]
public sealed class ChadoCombatRules : NinjaSlayerCombatSingletonTemplate
{
    private const int DiscardCost = 3;

    public override async Task AfterEnergyReset(Player player)
    {
        foreach (ChadoCard chado in ChadoCardsInHand(player).ToList())
        {
            await PlayerCmd.GainEnergy(chado.DynamicVars.Energy.IntValue, player);
        }
    }

    public override async Task AfterEnergySpent(CardModel card, int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        ChadoCard? chado = ChadoCardsInHand(card.Owner).FirstOrDefault();
        if (chado == null)
        {
            return;
        }

        int currentCost = chado.EnergyCost.GetWithModifiers(CostModifiers.Local);
        int newCost = Math.Min(DiscardCost, currentCost + amount);
        chado.EnergyCost.SetThisTurnOrUntilPlayed(newCost);
        NCard.FindOnTable(chado)?.PlayRandomizeCostAnim();

        if (newCost >= DiscardCost)
        {
            await CardPileCmd.Add(chado, PileType.Discard);
        }
    }

    public override Task AfterCardDrawn(
        PlayerChoiceContext choiceContext,
        CardModel card,
        bool fromHandDraw)
    {
        ResetCostWhenEnteringHand(card);
        return Task.CompletedTask;
    }

    public override Task AfterCardGeneratedForCombat(CardModel card, Player? creator)
    {
        ResetCostWhenEnteringHand(card);
        return Task.CompletedTask;
    }

    private static IEnumerable<ChadoCard> ChadoCardsInHand(Player player) =>
        PileType.Hand.GetPile(player).Cards.OfType<ChadoCard>();

    private static void ResetCostWhenEnteringHand(CardModel card)
    {
        if (card is not ChadoCard chado || card.Pile?.Type != PileType.Hand)
        {
            return;
        }

        chado.EnergyCost.SetThisTurnOrUntilPlayed(0);
        NCard.FindOnTable(chado)?.PlayRandomizeCostAnim();
    }
}
