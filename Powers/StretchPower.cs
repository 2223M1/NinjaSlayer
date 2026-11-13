#if NINJASLAYER_CHANNEL_STABLE
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace NinjaSlayer.Powers;

// The stable host predates Stretch. Match 0.113's card-scoped block bonus and consumption.
public sealed class StretchPower : NinjaSlayerCounterPower
{
    private sealed class Data
    {
        public CardPlay? CardPlay;
        public int AmountAtStart;
        public bool GrantedBlock;
    }

    protected override object InitInternalData() => new Data();
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.Static(StaticHoverTip.Block)];

    public override Task BeforeCardPlayed(CardPlay cardPlay)
    {
        Data data = GetInternalData<Data>();
        if (cardPlay.Card.Owner.Creature == Owner && cardPlay.Card.GainsBlock && data.CardPlay is null)
        {
            data.CardPlay = cardPlay;
            data.AmountAtStart = Amount;
        }
        return Task.CompletedTask;
    }

    public override decimal ModifyBlockAdditive(Creature target, decimal block, ValueProp props,
        CardModel? cardSource, CardPlay? cardPlay)
    {
        if (cardSource is null || cardSource.Owner != Owner.Player || !props.IsPoweredCardOrMonsterMoveBlock())
            return 0;
        Data data = GetInternalData<Data>();
        if (data.CardPlay is not null && data.CardPlay != cardPlay) return 0;
        if (cardPlay is null) return Amount;
        if (data.CardPlay is null || data.CardPlay.Card.Owner != cardSource.Owner) return 0;
        data.GrantedBlock = true;
        return Amount;
    }

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        Data data = GetInternalData<Data>();
        if (cardPlay != data.CardPlay) return;
        data.CardPlay = null;
        if (data.GrantedBlock)
        {
            data.GrantedBlock = false;
            await PowerCmd.ModifyAmount(choiceContext, this, -data.AmountAtStart, null, null);
        }
    }
}
#endif
