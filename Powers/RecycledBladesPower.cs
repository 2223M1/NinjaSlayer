using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using NinjaSlayer.Content;
using NinjaSlayer.Orbs;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class RecycledBladesPower : RedesignV1CounterPower, IRedesignScryListener
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named("ExhaustForShurikenPower");
    public async Task AfterScry(PlayerChoiceContext choiceContext, int viewed, int discarded)
    {
        Flash();
        await ShurikenOrb.AddStock(choiceContext, Owner.Player!, Amount);
    }
}
