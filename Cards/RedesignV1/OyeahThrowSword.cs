using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Content;
using NinjaSlayer.Orbs;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class OyeahThrowSword : RedesignV1UncommonCard
{
    public OyeahThrowSword() : base(nameof(OyeahThrowSword), "ShurikenBarrage", 2, CardType.Skill, TargetType.Self) { }
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Sly];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Stock", 1)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromOrb<ShurikenOrb>()];
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (ShurikenOrb.Find(Owner) is { } orb)
            await orb.FireConsumedVolley(choiceContext, 1, this);
        await ShurikenOrb.AddStock(choiceContext, Owner, DynamicVars["Stock"].IntValue);
    }
    protected override void OnUpgrade() => DynamicVars["Stock"].UpgradeValueBy(1);
}
