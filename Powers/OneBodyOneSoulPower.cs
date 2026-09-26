using NinjaSlayer.Content;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using STS2RitsuLib.Scaffolding.Content;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Code.Nodes;

namespace NinjaSlayer.Powers;

public sealed class OneBodyOneSoulPower : RedesignV1CounterPower
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(OneBodyOneSoulPower));

    public override Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        NarakuVisualOverlay.Sync(Owner);
        return Task.CompletedTask;
    }

    public override Task AfterRemoved(Creature oldOwner)
    {
        NarakuVisualOverlay.Sync(oldOwner);
        return Task.CompletedTask;
    }

    internal async Task AfterKarate(PlayerChoiceContext choiceContext)
    {
        Flash();
        await PowerCmd.Apply<NarakuLifePower>(choiceContext, Owner, Amount, Owner, null);
    }
}
