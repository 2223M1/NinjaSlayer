using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Relics;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Relics;

public sealed class MaguroSushiRelic : NinjaSlayerRelicTemplate
{
    public override RelicRarity Rarity => RelicRarity.Rare;
    public override bool HasUponPickupEffect => true;

    public override async Task AfterObtained()
    {
        Flash();
        await CreatureCmd.Heal(Owner.Creature, Owner.Creature.MaxHp);
    }
}
