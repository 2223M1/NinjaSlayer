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

public sealed class MasochisticBliss : NinjaSlayerCardTemplate
{
    private static readonly NinjaSlayerCardSpec CardSpec = new(nameof(MasochisticBliss), 1, CardType.Power, CardRarity.Uncommon, TargetType.Self, true, "BloodTears");


    // ponytail: reuse debuff-themed art until this card gets dedicated art.

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new VigorAmountVar(3)
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromPower<VigorPower>()
    ];

    public MasochisticBliss() : base(CardSpec) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<MasochisticBlissPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars.VigorAmount().IntValue,
            Owner.Creature,
            this);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.VigorAmount().UpgradeValueBy(1);
    }
}
