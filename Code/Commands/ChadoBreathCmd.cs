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

        CardPile draw = PileType.Draw.GetPile(player);
        CardPile hand = PileType.Hand.GetPile(player);
        CardPile discard = PileType.Discard.GetPile(player);
        List<ChadoEnergyRedesignV1> cards =
        [
            .. draw.Cards.OfType<ChadoEnergyRedesignV1>(),
            .. hand.Cards.OfType<ChadoEnergyRedesignV1>(),
            .. discard.Cards.OfType<ChadoEnergyRedesignV1>()
        ];

        bool generated = cards.Count == 0;
        if (generated)
        {
            ICombatState combatState = player.Creature.CombatState
                ?? throw new InvalidOperationException("Chado Breathing requires combat.");
            ChadoEnergyRedesignV1 card = combatState.CreateCard<ChadoEnergyRedesignV1>(player);
            await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, player);
            cards.Add(card);
        }

        int increase = RedesignV1Rules.ResolveChadoBreathIncrease(amount, !generated);
        foreach (ChadoEnergyRedesignV1 card in cards)
        {
            card.IncreaseEnergy(increase);
        }

        PlayForgeFeedback(cards);
    }

    private static void PlayForgeFeedback(IReadOnlyCollection<ChadoEnergyRedesignV1> cards)
    {
        if (cards.Count == 0 || !LocalContext.IsMine(cards.First()) || NCombatRoom.Instance is not { } room)
        {
            return;
        }

        SfxCmd.Play(ForgeSfx);
        foreach (ChadoEnergyRedesignV1 card in cards)
        {
            if (card.Pile?.Type == PileType.Hand && room.Ui.Hand.GetCard(card) is { } node)
            {
                NRun.Instance?.GlobalUi.AboveTopBarVfxContainer.AddChildSafely(
                    NCardSmithVfx.Create(node, playSfx: false));
            }
            else
            {
                NRun.Instance?.GlobalUi.CardPreviewContainer.AddChildSafely(
                    NCardSmithVfx.Create([card], playSfx: false));
            }
        }
    }
}
