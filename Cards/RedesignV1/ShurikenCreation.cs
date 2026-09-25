using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Content;
using NinjaSlayer.Orbs;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class ShurikenCreation : RedesignV1UncommonCard
{
    public ShurikenCreation() : base(nameof(ShurikenCreation), nameof(ShurikenCreation), 2, CardType.Skill, TargetType.Self) { }
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Sly];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Scry", 1), new DynamicVar("Stock", 3)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromKeyword(NinjaSlayerKeywords.Scry), HoverTipFactory.FromOrb<ShurikenOrb>()];
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await ScryCmd.Execute(choiceContext, Owner, DynamicVars["Scry"].IntValue);
        await ShurikenOrb.AddStock(choiceContext, Owner, DynamicVars["Stock"].IntValue);
    }
    protected override void OnUpgrade()
    {
        DynamicVars["Scry"].UpgradeValueBy(1);
        DynamicVars["Stock"].UpgradeValueBy(1);
    }
}
