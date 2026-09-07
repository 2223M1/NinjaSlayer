using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class SipTea : RedesignV1UncommonCard
{
    public SipTea() : base(nameof(SipTea), "DrinkTea", 1, CardType.Skill, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (IsUpgraded) await ChadoBreathCmd.Apply(Owner, 1);
        await PowerCmd.Apply<SipTeaPower>(choiceContext, Owner.Creature, 3, Owner.Creature, this);
    }
    protected override void OnUpgrade() { }
}
