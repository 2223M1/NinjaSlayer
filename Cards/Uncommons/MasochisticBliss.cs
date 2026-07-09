using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Powers;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

[RegisterCard(typeof(NinjaSlayerCardPool))]
public sealed class MasochisticBliss : ModCardTemplate
{
    private const int energyCost = 1;
    private const CardType type = CardType.Power;
    private const CardRarity rarity = CardRarity.Uncommon;
    private const TargetType targetType = TargetType.Self;
    private const bool shouldShowInCardLibrary = true;

    // ponytail: reuse debuff-themed art until this card gets dedicated art.
    public override CardAssetProfile AssetProfile => NinjaSlayerCardAssets.Named("BloodTears");

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DynamicVar("Vigor", 3)
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromPower<VigorPower>()
    ];

    public MasochisticBliss() : base(energyCost, type, rarity, targetType, shouldShowInCardLibrary) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<MasochisticBlissPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars["Vigor"].IntValue,
            Owner.Creature,
            this);
    }

    protected override void OnUpgrade()
    {
        DynamicVars["Vigor"].UpgradeValueBy(1);
    }
}
