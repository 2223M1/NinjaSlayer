using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class ScryDrawPower : RedesignV1CounterPower
{
    public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;
    public override int DisplayAmount => DynamicVars["CardsLeft"].IntValue;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("CardsLeft", 0)];
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(ScryDrawPower));

    public override Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        DynamicVars["CardsLeft"].BaseValue = Amount;
        InvokeDisplayAmountChanged();
        return Task.CompletedTask;
    }

    public override async Task AfterCardDiscarded(PlayerChoiceContext choiceContext, CardModel card)
    {
        if (card.Owner != Owner.Player) return;
        DynamicVars["CardsLeft"].BaseValue--;
        bool draw = DynamicVars["CardsLeft"].IntValue == 0;
        // Reset before drawing: draws can synchronously cause another discard.
        if (draw) DynamicVars["CardsLeft"].BaseValue = Amount;
        InvokeDisplayAmountChanged();
        if (draw)
        {
            Flash();
            await CardPileCmd.Draw(choiceContext, 1, Owner.Player!);
        }
    }
}
