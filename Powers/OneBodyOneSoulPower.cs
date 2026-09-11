using NinjaSlayer.Content;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class OneBodyOneSoulPower : RedesignV1CounterPower
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named("NarakuPower");

    internal async Task AfterBreathing(PlayerChoiceContext choiceContext)
    {
        Flash();
        await PowerCmd.Apply<KaratePower>(choiceContext, Owner, Amount, Owner, null);
    }
}
