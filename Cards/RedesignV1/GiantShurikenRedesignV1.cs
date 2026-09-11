using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class GiantShurikenRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromOrb<NinjaSlayer.Orbs.ShurikenOrb>(), .. HoverTipFactory.FromCardWithCardHoverTips<StrongShurikenTokenRedesignV1>()];

    public GiantShurikenRedesignV1()
        : base(nameof(GiantShurikenRedesignV1), "StarlessNight", 1, CardType.Power, TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PowerCmd.Apply<StarlessNightRedesignPower>(choiceContext, Owner.Creature, 1, Owner.Creature, this);

    protected override void OnUpgrade() => AddKeyword(CardKeyword.Retain);
}
