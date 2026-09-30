using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.Standard;

public sealed class StarlessNight : NinjaSlayerUncommonCard
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromOrb<NinjaSlayer.Orbs.ShurikenOrb>(), .. HoverTipFactory.FromCardWithCardHoverTips<StrongShuriken>()];

    public StarlessNight()
        : base(nameof(StarlessNight), 2, CardType.Power, TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PowerCmd.Apply<StarlessNightPower>(choiceContext, Owner.Creature, 1, Owner.Creature, this);

    protected override void OnUpgrade() => AddKeyword(CardKeyword.Innate);
}
