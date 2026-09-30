using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class RetainChadoPower : NinjaSlayerCounterPower
{
    public override PowerAssetProfile AssetProfile =>
        NinjaSlayerPowerAssets.Named(nameof(RetainChadoPower));

    public override Task BeforeFlush(PlayerChoiceContext choiceContext, Player player)
    {
        if (player == Owner.Player)
        {
            foreach (Chado chado in PileType.Hand.GetPile(player).Cards
                         .OfType<Chado>())
            {
                chado.GiveSingleTurnRetain();
            }
        }

        return Task.CompletedTask;
    }

    public override async Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants)
    {
        if (participants.Contains(Owner))
        {
            await PowerCmd.Decrement(this);
        }
    }
}
