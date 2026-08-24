using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace NinjaSlayer.Cards.RedesignV1;

public abstract partial class NinjaSlayerRedesignCardTemplate
{
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
    protected override PileType GetResultPileTypeForCardPlay()
    {
        PileType pileType = base.GetResultPileTypeForCardPlay();
        return pileType == PileType.Discard
            && (this is IReturnToHandAfterPlay || RedesignRepeatState.Has(this))
                ? PileType.Hand
                : pileType;
    }
#else
    protected override CardLocation GetResultLocationForCardPlay()
    {
        CardLocation result = base.GetResultLocationForCardPlay();
        if (result.pileType != PileType.Discard)
        {
            return result;
        }

        if (this is IReturnToHandAfterPlay || RedesignRepeatState.Has(this))
        {
            result.pileType = PileType.Hand;
        }

        return result;
    }
#endif
}
