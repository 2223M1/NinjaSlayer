using MegaCrit.Sts2.Core.Entities.Relics;

namespace NinjaSlayer.Relics;

public sealed class DeepChadoBreathingRelic : ChadoBreathingRelic
{
    protected override int ChadoCount => 2;
    protected override int BreathAmount => 2;

    public override RelicRarity Rarity => RelicRarity.Ancient;
}
