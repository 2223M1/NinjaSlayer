using MegaCrit.Sts2.Core.Models;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Content;

public static class NinjaSlayerRelicAssets
{
    public static RelicAssetProfile For(RelicModel relic) => Named(relic.GetType().Name);

    public static RelicAssetProfile For<TRelic>() where TRelic : RelicModel => Named(typeof(TRelic).Name);

    public static RelicAssetProfile Named(string relicImageName) => new(
        IconPath: NinjaSlayerAssetPaths.RelicImage($"{relicImageName}.png"),
        IconOutlinePath: NinjaSlayerAssetPaths.RelicImage($"{relicImageName}_outline.png"),
        BigIconPath: NinjaSlayerAssetPaths.RelicImage($"{relicImageName}_large.png"));

    public static RelicAssetProfile FromCardImage(string cardImageName) => new(
        IconPath: NinjaSlayerAssetPaths.CardImage($"{cardImageName}.png"),
        IconOutlinePath: NinjaSlayerAssetPaths.CardImage($"{cardImageName}.png"),
        BigIconPath: NinjaSlayerAssetPaths.CardImage($"{cardImageName}.png"));
}
