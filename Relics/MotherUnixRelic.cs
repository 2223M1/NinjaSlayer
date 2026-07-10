using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Relics;

public sealed class MotherUnixRelic : NinjaSlayerRelicTemplate
{
    public override RelicRarity Rarity => RelicRarity.Ancient;

    // ponytail: reuse the existing terminal relic art until Nancy gets dedicated icons.
    public override RelicAssetProfile AssetProfile => NinjaSlayerRelicAssets.For<PortableIrcTerminalRelic>();

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new CardsVar(3)
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(NinjaSlayerKeywords.Scry)
    ];

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (player != Owner)
        {
            return;
        }

        Flash();
        await ScryCmd.Execute(choiceContext, Owner, DynamicVars.Cards.IntValue);
    }
}
