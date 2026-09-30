using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.Standard;

public sealed class NarakuForm : NinjaSlayerRareCard
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromCard<BlackFlame>()];

    public NarakuForm()
        : base(nameof(NarakuForm), 3, CardType.Power, TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PowerCmd.Apply<NarakuFormPower>(
            choiceContext,
            Owner.Creature,
            1,
            Owner.Creature,
            this);

    protected override void OnUpgrade() => AddKeyword(CardKeyword.Retain);
}
