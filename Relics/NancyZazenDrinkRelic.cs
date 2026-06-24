using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models.Cards;
using NinjaSlayer.Cards;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Relics;

[RegisterRelic(typeof(NinjaSlayerRelicPool))]
public sealed class NancyZazenDrinkRelic : ModRelicTemplate
{
    public override RelicRarity Rarity => RelicRarity.Ancient;
    public override bool HasUponPickupEffect => true;

    // ponytail: reuse the existing terminal relic art until Nancy gets dedicated icons.
    public override RelicAssetProfile AssetProfile => new(
        IconPath: "res://NinjaSlayer/images/relics/PortableIrcTerminalRelic.png",
        IconOutlinePath: "res://NinjaSlayer/images/relics/PortableIrcTerminalRelic_outline.png",
        BigIconPath: "res://NinjaSlayer/images/relics/PortableIrcTerminalRelic_large.png"
    );

    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        HoverTipFactory.FromCardWithCardHoverTips<ZazenDrink>()
            .Concat(HoverTipFactory.FromCardWithCardHoverTips<PoorSleep>());

    public override async Task AfterObtained()
    {
        List<CardPileAddResult> results = new();
        results.Add(await CardPileCmd.Add(Owner.RunState.CreateCard<ZazenDrink>(Owner), PileType.Deck));
        results.Add(await CardPileCmd.Add(Owner.RunState.CreateCard<PoorSleep>(Owner), PileType.Deck));
        results.Add(await CardPileCmd.Add(Owner.RunState.CreateCard<PoorSleep>(Owner), PileType.Deck));
        CardCmd.PreviewCardPileAdd(results, 2f);
    }
}
