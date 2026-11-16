using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;
#if !NINJASLAYER_CHANNEL_STABLE
using StretchPower = MegaCrit.Sts2.Core.Models.Powers.StretchPower;
#endif

namespace NinjaSlayer.Powers;

public sealed class ComposurePower : NinjaSlayerCounterPower
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named("DiscardDefensePower");
    public override async Task AfterCardDiscarded(PlayerChoiceContext choiceContext, CardModel card)
    {
        if (card.Owner != Owner.Player) return;
        Flash();
        await PowerCmd.Apply<StretchPower>(choiceContext, Owner, Amount, Owner, null);
    }
}
