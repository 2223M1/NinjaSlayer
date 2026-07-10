using MegaCrit.Sts2.Core.Entities.Relics;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;

namespace NinjaSlayer.Relics;

public sealed class DeepChadoBreathingRelic : ChadoBreathingRelic
{
    protected override int HealAmount => 4;
    protected override int MaxHealPerCombat => 24;
    public override RelicRarity Rarity => RelicRarity.Ancient;
}
