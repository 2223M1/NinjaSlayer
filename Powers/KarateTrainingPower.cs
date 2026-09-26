using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class KarateTrainingPower : RedesignV1CounterPower
{
    public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(KarateTrainingPower));

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (player == Owner.Player)
        {
            Flash();
            await PowerCmd.Apply<KaratePower>(choiceContext, Owner, Amount, Owner, null);
            await CardCmd.Discard(choiceContext, await CardSelectCmd.FromHandForDiscard(
                choiceContext, player,
                new CardSelectorPrefs(CardSelectorPrefs.DiscardSelectionPrompt, 1),
                null, this));
        }
    }
}
