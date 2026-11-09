using MegaCrit.Sts2.Core.HoverTips;
using NinjaSlayer.Content;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Orbs;

namespace NinjaSlayer.Cards.Standard;

public sealed class ShurikenStorm : NinjaSlayerRareCard
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromOrb<NinjaSlayer.Orbs.ShurikenOrb>()];

    public ShurikenStorm() : base(nameof(ShurikenStorm), 1, CardType.Skill, TargetType.Self) { }
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var cards = PileType.Hand.GetPile(Owner).Cards.ToArray();
        await CardCmd.Discard(choiceContext, cards);
        await ShurikenOrb.AddStock(choiceContext, Owner, cards.Length);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}
