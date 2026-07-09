using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using NinjaSlayer.Cards;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Relics;

[RegisterRelic(typeof(NinjaSlayerRelicPool))]
public sealed class PortableIrcTerminalRelic : ModRelicTemplate
{
    public override RelicRarity Rarity => RelicRarity.Uncommon;

    public override RelicAssetProfile AssetProfile => NinjaSlayerRelicAssets.For(this);

    public override async Task BeforeHandDraw(MegaCrit.Sts2.Core.Entities.Players.Player player, PlayerChoiceContext choiceContext, MegaCrit.Sts2.Core.Combat.ICombatState combatState)
    {
        if (player == Owner && Owner.PlayerCombatState?.TurnNumber == 1)
        {
            Flash();
            await NinjaSlayerActions.AddGeneratedShuriken(choiceContext, Owner, 1, MegaCrit.Sts2.Core.Entities.Cards.PileType.Hand);
        }
    }
}
