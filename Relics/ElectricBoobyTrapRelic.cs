using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Relics;

[RegisterRelic(typeof(NinjaSlayerRelicPool))]
public sealed class ElectricBoobyTrapRelic : ModRelicTemplate
{
    public override RelicRarity Rarity => RelicRarity.Ancient;

    // ponytail: reuse the existing terminal relic art until Nancy gets dedicated icons.
    public override RelicAssetProfile AssetProfile => NinjaSlayerRelicAssets.For<PortableIrcTerminalRelic>();

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (Owner is not { } owner || player != owner || owner.PlayerCombatState?.TurnNumber != 1)
        {
            return;
        }

        Flash();
        await PlayerCmd.LoseEnergy(2, owner);
        if (owner.Creature.CombatState is not { } combatState)
        {
            return;
        }

        foreach (Creature enemy in combatState.HittableEnemies)
        {
            await CreatureCmd.Stun(enemy);
        }
    }
}
