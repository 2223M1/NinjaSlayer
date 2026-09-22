using MegaCrit.Sts2.Core.HoverTips;
using NinjaSlayer.Content;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class ReturnReturnReturnRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [.. HoverTipFactory.FromCardWithCardHoverTips<BlackFlameRedesignV1>(),
            HoverTipFactory.FromPower<MegaCrit.Sts2.Core.Models.Powers.StrengthPower>()];

    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<MegaCrit.Sts2.Core.Models.Powers.StrengthPower>(2)];
    public ReturnReturnReturnRedesignV1()
        : base(nameof(ReturnReturnReturnRedesignV1), "AssassinationFist", 1, CardType.Power, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<ReturnReturnReturnPower>(choiceContext, Owner.Creature,
            DynamicVars["StrengthPower"].BaseValue, Owner.Creature, this);
    }

    protected override void OnUpgrade() => DynamicVars["StrengthPower"].UpgradeValueBy(1);
}
