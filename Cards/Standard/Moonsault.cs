using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using NinjaSlayer.Orbs;

namespace NinjaSlayer.Cards.Standard;

public sealed class Moonsault : NinjaSlayerUncommonCard
{
    public Moonsault() : base(nameof(Moonsault), 1, CardType.Skill, TargetType.Self) { }
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Retain];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromOrb<ShurikenOrb>()];
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (ShurikenOrb.Find(Owner) is { } orb)
            await orb.FireConsumedVolley(choiceContext, 2, this);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}
