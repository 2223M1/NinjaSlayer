using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class MeditationPower : NinjaSlayerCounterPower
{
    public override PowerAssetProfile AssetProfile =>
        NinjaSlayerPowerAssets.Named("DrinkTeaPower");

    public override Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        foreach (CardModel card in Owner.Player!.PlayerCombatState!.AllCards.OfType<Chado>())
            CardCmd.ApplyKeyword(card, CardKeyword.Retain);
        return Task.CompletedTask;
    }

    public override Task AfterCardEnteredCombat(CardModel card)
    {
        if (card.Owner == Owner.Player && card is Chado)
            CardCmd.ApplyKeyword(card, CardKeyword.Retain);
        return Task.CompletedTask;
    }

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (player != Owner.Player)
        {
            return;
        }

        Flash();
        await ChadoBreathCmd.Apply(choiceContext, player, Amount);
    }
}
