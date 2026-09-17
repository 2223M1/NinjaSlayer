using MegaCrit.Sts2.Core.HoverTips;
using NinjaSlayer.Content;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class SipTea : RedesignV1UncommonCard
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [NinjaSlayerHoverTips.ChadoBreathing, .. HoverTipFactory.FromCardWithCardHoverTips<ChadoEnergyRedesignV1>()];

    public SipTea() : base(nameof(SipTea), nameof(SipTea), 1, CardType.Skill, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (IsUpgraded) await ChadoBreathCmd.Apply(choiceContext, Owner, 1);
        await PowerCmd.Apply<SipTeaPower>(choiceContext, Owner.Creature, 3, Owner.Creature, this);
    }
    protected override void OnUpgrade() { }
}
