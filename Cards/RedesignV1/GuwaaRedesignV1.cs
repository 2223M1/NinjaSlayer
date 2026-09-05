using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Commands;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class GuwaaRedesignV1 : RedesignV1UncommonCard
{
    public override bool GainsBlock => true;
    protected override bool IsPlayable =>
        PileType.Hand.GetPile(Owner).Cards.OfType<ChadoEnergyRedesignV1>().Any();
    protected override bool ShouldGlowGoldInternal => IsPlayable;
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new BlockVar(4, ValueProp.Move), new DynamicVar("Breath", 1)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromCard<ChadoEnergyRedesignV1>()];

    public GuwaaRedesignV1()
        : base(nameof(GuwaaRedesignV1), "IBlock", 0, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
        await ChadoBreathCmd.Apply(Owner, DynamicVars["Breath"].IntValue);
    }

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(3);
}
