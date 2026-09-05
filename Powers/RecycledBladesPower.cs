using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Content;
using NinjaSlayer.Orbs;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class RecycledBladesPower : RedesignV1CounterPower
{
    public override PowerAssetProfile AssetProfile =>
        NinjaSlayerPowerAssets.Named("ExhaustForShurikenPower");

    public override Task AfterCardDiscarded(PlayerChoiceContext choiceContext, CardModel card)
    {
        if (card.Owner != Owner.Player || ShurikenOrb.Find(Owner.Player!) is not null)
        {
            return Task.CompletedTask;
        }

        return AddStockAfterDiscard(choiceContext);
    }

    internal async Task AddStockAfterDiscard(PlayerChoiceContext choiceContext)
    {
        Flash();
        await ShurikenOrb.AddStock(choiceContext, Owner.Player!, Amount);
    }
}
