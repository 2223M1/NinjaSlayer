using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using NinjaSlayer.Orbs;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Relics;

public sealed class PortableIrcTerminalRelic : NinjaSlayerRelicTemplate
{
    public override RelicRarity Rarity => RelicRarity.Uncommon;

    public override async Task BeforeHandDraw(MegaCrit.Sts2.Core.Entities.Players.Player player, PlayerChoiceContext choiceContext, MegaCrit.Sts2.Core.Combat.ICombatState combatState)
    {
        if (player == Owner && Owner.PlayerCombatState?.TurnNumber == 1)
        {
            Flash();
            await ShurikenOrb.AddStock(choiceContext, Owner, 1);
        }
    }
}
