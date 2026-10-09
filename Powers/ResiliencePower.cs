using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class ResiliencePower : NinjaSlayerCounterPower
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(ResiliencePower));
    public override async Task AfterCardGeneratedForCombat(CardModel card, Player? creator)
    {
        if (creator?.Creature != Owner || card.Type != CardType.Status) return;
        Flash();
        // Drawing can shuffle into Stratagem or autoplay a card that asks for a
        // choice. Let the native hook action synchronize that choice on all peers.
        var context = new HookPlayerChoiceContext(this, LocalContext.NetId!.Value, CombatState, GameActionType.Combat);
        await context.AssignTaskAndWaitForPauseOrCompletion(CardPileCmd.Draw(context, Amount, creator));
    }
}
