using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Powers;
using NinjaSlayer.Orbs;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class HiddenEdgeRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DynamicVar("Stock", 2), new PowerVar<FocusPower>(3)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromOrb<ShurikenOrb>(), HoverTipFactory.FromPower<FocusPower>()];

    public HiddenEdgeRedesignV1()
        : base(nameof(HiddenEdgeRedesignV1), "ShurikenStock", 1, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await ShurikenOrb.AddStock(choiceContext, Owner, DynamicVars["Stock"].IntValue);
        await PowerCmd.Apply<HiddenEdgeTemporaryFocusPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars[nameof(FocusPower)].BaseValue,
            Owner.Creature,
            this);
    }

    protected override void OnUpgrade() => DynamicVars[nameof(FocusPower)].UpgradeValueBy(1);
}
