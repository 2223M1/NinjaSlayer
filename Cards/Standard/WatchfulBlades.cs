using MegaCrit.Sts2.Core.HoverTips;
using NinjaSlayer.Content;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.Standard;

public sealed class WatchfulBlades : NinjaSlayerRareCard
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromOrb<NinjaSlayer.Orbs.ShurikenOrb>()];

    public WatchfulBlades()
        : base(nameof(WatchfulBlades), 1, CardType.Power, TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PowerCmd.Apply<WatchfulBladesPower>(choiceContext, Owner.Creature, 1, Owner.Creature, this);

    protected override void OnUpgrade() => AddKeyword(CardKeyword.Innate);
}
