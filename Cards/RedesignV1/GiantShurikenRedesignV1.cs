using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class GiantShurikenRedesignV1 : RedesignV1RareCard
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromCard<StrongShurikenTokenRedesignV1>(), HoverTipFactory.FromPower<StarlessNightRedesignPower>()];

    public GiantShurikenRedesignV1()
        : base(nameof(GiantShurikenRedesignV1), "StarlessNight", 2, CardType.Power, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        StarlessNightRedesignPower? power = await PowerCmd.Apply<StarlessNightRedesignPower>(
            choiceContext,
            Owner.Creature,
            1,
            Owner.Creature,
            this);
        if (power != null && IsUpgraded)
        {
            power.GenerateUpgradedToken = true;
        }
    }

    protected override void OnUpgrade() { }
}
