using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class TeaTeaPower : RedesignV1CounterPower
{
    public override PowerAssetProfile AssetProfile =>
        NinjaSlayerPowerAssets.Named("EndTurnRetainPower");

    public override Task BeforeFlush(PlayerChoiceContext choiceContext, Player player)
    {
        if (player == Owner.Player)
        {
            foreach (ChadoEnergyRedesignV1 chado in PileType.Hand.GetPile(player).Cards
                         .OfType<ChadoEnergyRedesignV1>())
            {
                chado.GiveSingleTurnRetain();
            }
        }

        return Task.CompletedTask;
    }

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (player != Owner.Player)
        {
            return;
        }

        Flash();
        await ChadoBreathCmd.Apply(player, Amount);
    }
}
