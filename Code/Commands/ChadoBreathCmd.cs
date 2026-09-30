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
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;

namespace NinjaSlayer.Code.Commands;

public static class ChadoBreathCmd
{
    private const string ForgeSfx = "event:/sfx/characters/regent/regent_refine";

    public static async Task Apply(PlayerChoiceContext choiceContext, Player player, int amount)
    {
        if (amount <= 0 || CombatManager.Instance.IsOverOrEnding)
        {
            return;
        }

        CardPile hand = PileType.Hand.GetPile(player);
        List<Chado> cards = hand.Cards
            .OfType<Chado>()
            .ToList();

        bool hasChadoInHand = cards.Count > 0;
        if (!hasChadoInHand)
        {
            ICombatState combatState = player.Creature.CombatState
                ?? throw new InvalidOperationException("Chado Breathing requires combat.");
            Chado card = combatState.CreateCard<Chado>(player);
            await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, player);
            cards.Add(card);
        }

        int increase = NinjaSlayerCardRules.ResolveChadoBreathIncrease(amount, hasChadoInHand);
        foreach (Chado card in cards)
        {
            card.IncreaseEnergy(increase);
        }

        PlayForgeFeedback(cards);
        Telemetry.NinjaSlayerCombatTelemetry.Mechanic("chado_breath", player.Creature, amount);
    }

    private static void PlayForgeFeedback(List<Chado> cards)
    {
        if (!LocalContext.IsMine(cards[0]) || NCombatRoom.Instance is not { } room)
        {
            return;
        }

        SfxCmd.Play(ForgeSfx);
        foreach (Chado card in cards)
        {
            if (room.Ui.Hand.GetCard(card) is { } node)
            {
                NRun.Instance?.GlobalUi.AboveTopBarVfxContainer.AddChildSafely(
                    NCardSmithVfx.Create(node, playSfx: false));
            }
        }
    }
}
