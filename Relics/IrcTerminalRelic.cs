using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using NinjaSlayer.Cards;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Relics;

[RegisterRelic(typeof(NinjaSlayerRelicPool))]
public sealed class IrcTerminalRelic : ModRelicTemplate
{
    public override RelicRarity Rarity => RelicRarity.Ancient;

    // ponytail: reuse the existing terminal relic art until Nancy gets dedicated icons.
    public override RelicAssetProfile AssetProfile => new(
        IconPath: "res://NinjaSlayer/images/relics/PortableIrcTerminalRelic.png",
        IconOutlinePath: "res://NinjaSlayer/images/relics/PortableIrcTerminalRelic_outline.png",
        BigIconPath: "res://NinjaSlayer/images/relics/PortableIrcTerminalRelic_large.png"
    );

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (player != Owner)
        {
            return;
        }

        Flash();
        await PlayerCmd.GainEnergy(1, Owner);
        await NinjaSlayerActions.AddGeneratedCard<BusyLine>(Owner, PileType.Hand);
    }
}
