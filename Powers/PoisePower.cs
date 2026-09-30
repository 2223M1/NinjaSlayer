using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class PoisePower : NinjaSlayerCounterPower
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(PoisePower));

    public override async Task AfterCardGeneratedForCombat(CardModel card, Player? creator)
    {
        if (creator?.Creature != Owner || card is not Chado)
        {
            return;
        }

        Flash();
        await PowerCmd.Apply<KaratePower>(
            new ThrowingPlayerChoiceContext(),
            Owner,
            Amount,
            Owner,
            card);
    }
}
