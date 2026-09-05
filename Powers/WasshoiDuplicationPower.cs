using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class WasshoiDuplicationPower : RedesignV1CounterPower
{
    private CardModel? _targetCard;

    public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named("EveryThirdAttackPower");

    public void Arm(CardModel card)
    {
        AssertMutable();
        _targetCard = card;
    }

    public override int ModifyCardPlayCount(CardModel card, Creature? target, int playCount) =>
        card == _targetCard ? playCount + Amount : playCount;

    public override Task AfterModifyingCardPlayCount(CardModel card) =>
        card == _targetCard ? PowerCmd.Remove(this) : Task.CompletedTask;
}
