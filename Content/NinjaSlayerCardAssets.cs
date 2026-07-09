using MegaCrit.Sts2.Core.Models;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Content;

public static class NinjaSlayerCardAssets
{
    public static CardAssetProfile For(CardModel card) => Named(card.GetType().Name);

    public static CardAssetProfile For<TCard>() where TCard : CardModel => Named(typeof(TCard).Name);

    public static CardAssetProfile Named(string cardImageName) => new(
        PortraitPath: NinjaSlayerAssetPaths.CardImage($"{cardImageName}.png"));
}
