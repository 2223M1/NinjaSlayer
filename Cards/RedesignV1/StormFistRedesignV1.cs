using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class StormFistRedesignV1 : RedesignV1RareCard
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromCard<FinisherRedesignV1>()];

    public StormFistRedesignV1()
        : base(nameof(StormFistRedesignV1), "TornadoFist", 2, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        FinisherRedesignV1 punch = CombatState!.CreateCard<FinisherRedesignV1>(Owner);
        if (IsUpgraded)
        {
            CardCmd.Upgrade(punch);
        }

        await CardPileCmd.AddGeneratedCardToCombat(punch, PileType.Hand, Owner);
    }

    protected override void OnUpgrade() { }
}
