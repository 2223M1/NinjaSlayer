using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class CrossGuardPower : NinjaSlayerPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named("OpeningGuardPower");

#if NINJASLAYER_CHANNEL_STABLE
    public override (PileType, CardPilePosition) ModifyCardPlayResultPileTypeAndPosition(
        CardModel card, bool isAutoPlay, ResourceInfo resources, PileType pileType, CardPilePosition position)
    {
        return card.Owner.Creature == Owner && !card.IsDupe
            ? (PileType.Hand, CardPilePosition.Top) : (pileType, position);
    }

    public override async Task AfterModifyingCardPlayResultPileOrPosition(
        CardModel card, PileType pileType, CardPilePosition position)
#else
    public override CardLocation ModifyCardPlayResultLocation(
        CardModel card, bool isAutoPlay, ResourceInfo resources, CardLocation cardLocation)
    {
        if (card.Owner.Creature == Owner && !card.IsDupe)
        {
            cardLocation.pileType = PileType.Hand;
            cardLocation.position = CardPilePosition.Top;
            cardLocation.player = card.Owner;
        }
        return cardLocation;
    }

    public override async Task AfterModifyingCardPlayResultLocation(CardModel card, CardLocation cardLocation)
#endif
    {
        Flash();
        await PowerCmd.Decrement(this);
    }

    public override async Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        if (participants.Contains(Owner)) await PowerCmd.Remove(this);
    }
}
