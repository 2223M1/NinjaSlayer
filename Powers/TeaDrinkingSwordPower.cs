using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Cards;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

[RegisterPower]
public sealed class TeaDrinkingSwordPower : ModPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.For(GetType());

    private int generatedShurikenCount;

    public override async Task AfterCardGeneratedForCombat(CardModel card, Player? creator)
    {
        if (creator == null || creator.Creature != Owner)
        {
            return;
        }

        if (!card.Tags.Contains(NinjaSlayerCardTags.Shuriken))
        {
            return;
        }

        generatedShurikenCount++;
        int threshold = (int)Amount;
        while (generatedShurikenCount >= threshold)
        {
            Player? owner = Owner.Player;
            if (owner == null)
            {
                return;
            }

            generatedShurikenCount -= threshold;
            Flash();
            await NinjaSlayerActions.AddGeneratedCard<ChadoCard>(
                owner,
                PileType.Draw,
                CardPilePosition.Random);
        }
    }
}
