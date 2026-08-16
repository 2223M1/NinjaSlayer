using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace NinjaSlayer.Cards.RedesignV1;

public abstract partial class NinjaSlayerRedesignCardTemplate
{
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
    protected Task MoveChopToDrawTopForLegacyHost() =>
        Keywords.Contains(CardKeyword.Exhaust) || ExhaustOnNextPlay
            ? Task.CompletedTask
            : CardPileCmd.Add(this, PileType.Draw, CardPilePosition.Top);

    protected override PileType GetResultPileTypeForCardPlay()
    {
        PileType pileType = base.GetResultPileTypeForCardPlay();
        return pileType == PileType.Discard
            && (this is IReturnToHandAfterPlay || RedesignRepeatState.Has(this))
                ? PileType.Hand
                : pileType;
    }
#else
    protected static Task MoveChopToDrawTopForLegacyHost() => Task.CompletedTask;

    protected override CardLocation GetResultLocationForCardPlay()
    {
        CardLocation result = base.GetResultLocationForCardPlay();
        if (result.pileType != PileType.Discard)
        {
            return result;
        }

        if (this is ChopRedesignV1)
        {
            result.pileType = PileType.Draw;
            result.position = CardPilePosition.Top;
        }
        else if (this is IReturnToHandAfterPlay || RedesignRepeatState.Has(this))
        {
            result.pileType = PileType.Hand;
        }

        return result;
    }
#endif
}
