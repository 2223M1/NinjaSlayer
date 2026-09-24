using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Content;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class DecidedOutcomeRedesignV1 : RedesignV1UncommonCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(2)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromKeyword(NinjaSlayerKeywords.Scry), NinjaSlayerHoverTips.ChadoBreathing, .. HoverTipFactory.FromCardWithCardHoverTips<ChadoEnergyRedesignV1>()];

    public DecidedOutcomeRedesignV1()
        : base(nameof(DecidedOutcomeRedesignV1), "KarateFinish", 1, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ScryResult result = await ScryCmd.Execute(
            choiceContext,
            Owner,
            DynamicVars.Cards.IntValue,
            exhaustDiscarded: true);
        await ChadoBreathCmd.Apply(choiceContext, Owner, result.ExhaustedCards);
    }

    protected override void OnUpgrade() => DynamicVars.Cards.UpgradeValueBy(1);
}
