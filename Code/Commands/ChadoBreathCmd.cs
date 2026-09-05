using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.Commands;

public static class ChadoBreathCmd
{
    private const string ForgeSfx = "event:/sfx/characters/regent/regent_refine";

    public static async Task Apply(Player player, int amount, AbstractModel? source)
    {
        if (amount <= 0 || CombatManager.Instance.IsOverOrEnding)
        {
            return;
        }

        CardPile hand = PileType.Hand.GetPile(player);
        List<ChadoEnergyRedesignV1> cards = hand.Cards
            .OfType<ChadoEnergyRedesignV1>()
            .ToList();

        bool hasChadoInHand = cards.Count > 0;
        if (!hasChadoInHand)
        {
            ICombatState combatState = player.Creature.CombatState
                ?? throw new InvalidOperationException("Chado Breathing requires combat.");
            ChadoEnergyRedesignV1 card = combatState.CreateCard<ChadoEnergyRedesignV1>(player);
            await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, player);
            cards.Add(card);
        }

        int increase = RedesignV1Rules.ResolveChadoBreathIncrease(amount, hasChadoInHand);
        foreach (ChadoEnergyRedesignV1 card in cards)
        {
            card.IncreaseEnergy(increase);
        }

        PlayForgeFeedback(cards);
    }

    private static void PlayForgeFeedback(List<ChadoEnergyRedesignV1> cards)
    {
        if (cards.Count == 0 || !LocalContext.IsMine(cards[0]) || NCombatRoom.Instance is not { } room)
        {
            return;
        }

        SfxCmd.Play(ForgeSfx);
        foreach (ChadoEnergyRedesignV1 card in cards)
        {
            if (room.Ui.Hand.GetCard(card) is { } node)
            {
                NRun.Instance?.GlobalUi.AboveTopBarVfxContainer.AddChildSafely(
                    NCardSmithVfx.Create(node, playSfx: false));
            }
        }
    }
}
