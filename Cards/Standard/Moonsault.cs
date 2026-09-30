using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Content;
using NinjaSlayer.Orbs;

namespace NinjaSlayer.Cards.Standard;

public sealed class Moonsault : NinjaSlayerUncommonCard
{
    public Moonsault() : base(nameof(Moonsault), 1, CardType.Skill, TargetType.Self) { }
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromOrb<ShurikenOrb>()];
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (ShurikenOrb.Find(Owner) is { } orb)
            await orb.FireConsumedVolley(choiceContext, 1, this);
    }
    protected override void OnUpgrade() => AddKeyword(CardKeyword.Retain);
}
