using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

[RegisterCard(typeof(NinjaSlayerCardPool))]
public sealed class ShurikenSpread : ModCardTemplate
{
    private const int energyCost = 1;
    private const CardType type = CardType.Skill;
    private const CardRarity rarity = CardRarity.Common;
    private const TargetType targetType = TargetType.Self;
    private const bool shouldShowInCardLibrary = true;

    public override CardAssetProfile AssetProfile => NinjaSlayerCardAssets.For(this);

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DynamicVar("Shuriken", 2)
    ];

    public ShurikenSpread() : base(energyCost, type, rarity, targetType, shouldShowInCardLibrary) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await NinjaSlayerActions.AddGeneratedShuriken(choiceContext, Owner, DynamicVars["Shuriken"].IntValue, PileType.Hand, IsUpgraded);
    }

    protected override void OnUpgrade() { }
}
