using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Orbs;

namespace NinjaSlayer.Cards.Standard;

public sealed class ShurikenCreation : NinjaSlayerUncommonCard
{
    public ShurikenCreation() : base(nameof(ShurikenCreation), 1, CardType.Skill, TargetType.Self) { }
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Sly];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Stock", 3)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromOrb<ShurikenOrb>()];
    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        ShurikenOrb.AddStock(choiceContext, Owner, DynamicVars["Stock"].IntValue);
    protected override void OnUpgrade() => DynamicVars["Stock"].UpgradeValueBy(1);
}
