using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class NarakuFormRedesignV1 : RedesignV1RareCard
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromCard<BlackFlameRedesignV1>()];

    public NarakuFormRedesignV1()
        : base(nameof(NarakuFormRedesignV1), nameof(NarakuFormRedesignV1), 3, CardType.Power, TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PowerCmd.Apply<NarakuFormRedesignPower>(
            choiceContext,
            Owner.Creature,
            1,
            Owner.Creature,
            this);

    protected override void OnUpgrade() { }
}
