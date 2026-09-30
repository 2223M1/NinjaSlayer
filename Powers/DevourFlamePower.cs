using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class DevourFlamePower : NinjaSlayerCounterPower
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(DevourFlamePower));
    public override Task AfterCardExhausted(PlayerChoiceContext choiceContext, CardModel card, bool causedByEthereal) =>
        card.Owner.Creature == Owner && card is BlackFlame
            ? PowerCmd.Apply<StrengthPower>(choiceContext, Owner, Amount, Owner, card)
            : Task.CompletedTask;
}
