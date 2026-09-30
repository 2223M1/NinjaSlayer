using MegaCrit.Sts2.Core.HoverTips;
using NinjaSlayer.Content;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.Standard;

public sealed class Sip : NinjaSlayerUncommonCard
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [NinjaSlayerHoverTips.ChadoBreathing, .. HoverTipFactory.FromCardWithCardHoverTips<Chado>()];

    public Sip() : base(nameof(Sip), 1, CardType.Skill, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (IsUpgraded) await ChadoBreathCmd.Apply(choiceContext, Owner, 1);
        await PowerCmd.Apply<SipPower>(choiceContext, Owner.Creature, 3, Owner.Creature, this);
    }
    protected override void OnUpgrade() { }
}
