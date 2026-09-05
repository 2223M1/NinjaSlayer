using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Powers;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class StarlessNightRedesignPower : NinjaSlayerPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named("StarlessNightPower");

    public bool GenerateUpgradedToken { get; set; }

    internal async Task GenerateStrongShuriken()
    {
        StrongShurikenTokenRedesignV1 card =
            CombatState.CreateCard<StrongShurikenTokenRedesignV1>(Owner.Player!);
        if (GenerateUpgradedToken)
        {
            CardCmd.Upgrade(card);
        }

        Flash();
        await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, Owner.Player!);
    }
}
