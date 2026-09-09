using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.HoverTips;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class PlaceholderGoldDefense01 : RedesignV1RareCard
{
    public PlaceholderGoldDefense01() : base(nameof(PlaceholderGoldDefense01), "BlockCard", 1, CardType.Skill, TargetType.Self) { }
    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(10, ValueProp.Move), new DynamicVar("ExtraBlock", 4)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => HoverTipFactory.FromCardWithCardHoverTips<ChadoEnergyRedesignV1>();
    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        CreatureCmd.GainBlock(Owner.Creature,
            DynamicVars.Block.BaseValue + PileType.Hand.GetPile(Owner).Cards.OfType<ChadoEnergyRedesignV1>().Count() * DynamicVars["ExtraBlock"].BaseValue,
            ValueProp.Move, cardPlay);
    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(1);
        DynamicVars["ExtraBlock"].UpgradeValueBy(2);
    }
}
