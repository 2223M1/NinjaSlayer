using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class LingeringMeleePower : RedesignV1CounterPower
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named("HellTornadoPower");

    public override async Task BeforeSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants)
    {
        if (side != Owner.Side || !participants.Contains(Owner))
        {
            return;
        }

        Flash();
        for (int index = 0; index < Amount; index++)
        {
            IReadOnlyList<CardModel> cards = PileType.Draw.GetPile(Owner.Player!).Cards;
            if (cards.Count == 0)
            {
                break;
            }

            await CardCmd.AutoPlay(choiceContext, cards[0], null);
        }
    }
}
