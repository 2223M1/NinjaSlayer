using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class ShurikenDrawPower : RedesignV1CounterPower
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named("ExhaustForShurikenPower");
    internal async Task AfterStockGained(PlayerChoiceContext choiceContext)
    {
        Flash();
        await CardPileCmd.Draw(choiceContext, Amount, Owner.Player!);
    }
}
