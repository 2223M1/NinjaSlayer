using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Cards;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards.RedesignV1;

public abstract partial class NinjaSlayerRedesignCardTemplate(
    NinjaSlayerCardSpec cardSpec,
    string legacyArtName) : NinjaSlayerStandaloneCardTemplate(cardSpec)
{
    public override CardAssetProfile AssetProfile => NinjaSlayerCardAssets.Named(legacyArtName);

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

public abstract class ArchivedRedesignV1Card(
    string id,
    string art,
    int cost,
    CardType type,
    CardRarity rarity,
    TargetType target) : NinjaSlayerRedesignCardTemplate(
        new NinjaSlayerCardSpec(id, cost, type, rarity, target, true),
        art);
