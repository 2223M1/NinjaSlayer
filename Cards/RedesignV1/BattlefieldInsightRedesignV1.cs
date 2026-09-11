using MegaCrit.Sts2.Core.HoverTips;
using NinjaSlayer.Content;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using NinjaSlayer.Powers;
using MegaCrit.Sts2.Core.Localization.DynamicVars;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class BattlefieldInsightRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromPower<ScryDrawPower>()];

    public BattlefieldInsightRedesignV1()
        : base(nameof(BattlefieldInsightRedesignV1), "Contraption", 1, CardType.Power, TargetType.Self) { }

    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("DiscardThreshold", 3)];

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PowerCmd.Apply<ScryDrawPower>(choiceContext, Owner.Creature, DynamicVars["DiscardThreshold"].BaseValue, Owner.Creature, this);

    protected override void OnUpgrade()
    {
        DynamicVars["DiscardThreshold"].UpgradeValueBy(-1);
        AddKeyword(CardKeyword.Retain);
    }
}
