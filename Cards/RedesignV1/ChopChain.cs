using MegaCrit.Sts2.Core.HoverTips;
using NinjaSlayer.Content;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class ChopChain : RedesignV1RareCard
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromPower<NinjaSlayer.Powers.KaratePower>(), .. NinjaSlayerHoverTips.ExhaustingChop(IsUpgraded)];

    public ChopChain() : base(nameof(ChopChain), "Chop", 2, CardType.Power, TargetType.Self) { }
    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var power = (ChopChainPower)ModelDb.Power<ChopChainPower>().ToMutable();
        power.UpgradedChop = IsUpgraded;
        return PowerCmd.Apply(choiceContext, power, Owner.Creature, 1, Owner.Creature, this);
    }
    protected override void OnUpgrade() { }
}
