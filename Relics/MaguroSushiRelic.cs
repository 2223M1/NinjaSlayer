using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Relics;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Relics;

[RegisterRelic(typeof(NinjaSlayerRelicPool))]
public sealed class MaguroSushiRelic : ModRelicTemplate
{
    public override RelicRarity Rarity => RelicRarity.Rare;
    public override bool HasUponPickupEffect => true;

    public override RelicAssetProfile AssetProfile => NinjaSlayerRelicAssets.For(this);

    public override async Task AfterObtained()
    {
        Flash();
        await CreatureCmd.Heal(Owner.Creature, Owner.Creature.MaxHp);
    }
}
