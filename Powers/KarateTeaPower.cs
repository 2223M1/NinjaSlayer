using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class KarateTeaPower : RedesignV1CounterPower
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named("TeaDrinkingSwordPower");

    public override async Task AfterCardGeneratedForCombat(CardModel card, Player? creator)
    {
        if (creator?.Creature != Owner || card is not ChadoEnergyRedesignV1)
        {
            return;
        }

        Flash();
        await PowerCmd.Apply<KaratePower>(
            new ThrowingPlayerChoiceContext(),
            Owner,
            Amount,
            Owner,
            card);
    }
}
