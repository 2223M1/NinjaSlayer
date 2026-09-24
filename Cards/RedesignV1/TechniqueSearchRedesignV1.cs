using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class TechniqueSearchRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(2)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromKeyword(CardKeyword.Sly)];
    public TechniqueSearchRedesignV1()
        : base(nameof(TechniqueSearchRedesignV1), nameof(TechniqueSearchRedesignV1), 1, CardType.Skill, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var drawn = await CardPileCmd.Draw(choiceContext, DynamicVars.Cards.IntValue, Owner);
        foreach (var card in drawn) CardCmd.ApplyKeyword(card, CardKeyword.Sly);
    }
    protected override void OnUpgrade() => DynamicVars.Cards.UpgradeValueBy(1);
}
