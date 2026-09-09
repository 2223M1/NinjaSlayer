using MegaCrit.Sts2.Core.HoverTips;
using NinjaSlayer.Content;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class KarateScry : RedesignV1RareCard
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        IsUpgraded ? [HoverTipFactory.FromPower<KaratePower>(), HoverTipFactory.FromKeyword(NinjaSlayerKeywords.Scry)] : [HoverTipFactory.FromPower<KaratePower>()];

    public KarateScry() : base(nameof(KarateScry), "ReadyBlade", 1, CardType.Power, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<KarateScryPower>(choiceContext, Owner.Creature, 1, Owner.Creature, this);
        if (IsUpgraded) await ScryCmd.Execute(choiceContext, Owner, 2);
    }
    protected override void OnUpgrade() { }
}
