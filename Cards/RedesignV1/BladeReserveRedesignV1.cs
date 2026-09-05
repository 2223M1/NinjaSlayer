using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Orbs;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class BladeReserveRedesignV1 : RedesignV1CommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DynamicVar("Stock", 2), new CardsVar(2)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromOrb<ShurikenOrb>()];

    public BladeReserveRedesignV1()
        : base(nameof(BladeReserveRedesignV1), "ShurikenCard", 1, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await ShurikenOrb.AddStock(choiceContext, Owner, DynamicVars["Stock"].IntValue);
        await CardPileCmd.Draw(choiceContext, DynamicVars.Cards.IntValue, Owner);
    }

    protected override void OnUpgrade() => DynamicVars["Stock"].UpgradeValueBy(1);
}
