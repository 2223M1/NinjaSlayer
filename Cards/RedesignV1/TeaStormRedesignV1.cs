using MegaCrit.Sts2.Core.HoverTips;
using NinjaSlayer.Content;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Commands;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class TeaStormRedesignV1 : RedesignV1RareCard
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [NinjaSlayerHoverTips.ChadoBreathing, .. HoverTipFactory.FromCardWithCardHoverTips<ChadoEnergyRedesignV1>()];

    protected override bool HasEnergyCostX => true;
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Retain, CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("BreathPerX", 2), new DynamicVar("ExtraX", 0)];

    public TeaStormRedesignV1()
        : base(nameof(TeaStormRedesignV1), "SteepTea", 0, CardType.Skill, TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        int x = ResolveEnergyXValue();
        return PowerCmd.Apply<PourTeaNextTurnPower>(choiceContext, Owner.Creature,
            x * DynamicVars["BreathPerX"].IntValue + DynamicVars["ExtraX"].IntValue, Owner.Creature, this);
    }

    protected override void OnUpgrade() => DynamicVars["ExtraX"].UpgradeValueBy(1);
}
