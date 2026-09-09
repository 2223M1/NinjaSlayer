using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.HoverTips;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class StarlessNightRedesignPower : NinjaSlayerPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named("StarlessNightPower");

    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        HoverTipFactory.FromCardWithCardHoverTips<StrongShurikenTokenRedesignV1>();

    internal async Task GenerateStrongShuriken(int damage)
    {
        StrongShurikenTokenRedesignV1 card =
            CombatState.CreateCard<StrongShurikenTokenRedesignV1>(Owner.Player!);
        card.SnapshotDamage = damage;

        Flash();
        await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, Owner.Player!);
    }
}
