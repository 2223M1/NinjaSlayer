using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class ScryDrawPower : RedesignV1CounterPower, IRedesignScryListener
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named("DiscardDefensePower");

    public async Task AfterScry(PlayerChoiceContext choiceContext, int viewed, int discarded)
    {
        Flash();
        await CardPileCmd.Draw(choiceContext, Amount, Owner.Player!);
    }
}
