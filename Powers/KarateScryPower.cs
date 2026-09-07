using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class KarateScryPower : RedesignV1CounterPower
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(KaratePower));
    public override Task AfterCardDiscarded(PlayerChoiceContext choiceContext, CardModel card) =>
        card.Owner.Creature == Owner
            ? PowerCmd.Apply<KaratePower>(choiceContext, Owner, Amount, Owner, card) : Task.CompletedTask;
}
