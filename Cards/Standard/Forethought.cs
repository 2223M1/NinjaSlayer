using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.Standard;

public sealed class Forethought : NinjaSlayerRareCard
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromKeyword(NinjaSlayerKeywords.Scry), HoverTipFactory.FromKeyword(CardKeyword.Sly)];
    public Forethought()
        : base(nameof(Forethought), 2, CardType.Power, TargetType.Self) { }
    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PowerCmd.Apply<ForethoughtPower>(choiceContext, Owner.Creature, 1, Owner.Creature, this);
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}
