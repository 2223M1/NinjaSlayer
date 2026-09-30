using NinjaSlayer.Content;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.Standard;

public sealed class Meditation : NinjaSlayerRareCard
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [NinjaSlayerHoverTips.ChadoBreathing, HoverTipFactory.FromKeyword(CardKeyword.Retain), .. HoverTipFactory.FromCardWithCardHoverTips<Chado>()];

    public Meditation()
        : base(nameof(Meditation), 2, CardType.Power, TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PowerCmd.Apply<MeditationPower>(choiceContext, Owner.Creature, 1, Owner.Creature, this);

    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}
