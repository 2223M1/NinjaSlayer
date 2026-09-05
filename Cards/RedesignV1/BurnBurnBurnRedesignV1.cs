using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class BurnBurnBurnRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("EnemyHpLoss", 8)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromCard<BlackFlameRedesignV1>()];

    public BurnBurnBurnRedesignV1()
        : base(nameof(BurnBurnBurnRedesignV1), "BloodTears", 1, CardType.Power, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await NinjaSlayerCardCmd.AddGeneratedCard<BlackFlameRedesignV1>(Owner, PileType.Hand);
        await PowerCmd.Apply<BurnBurnBurnPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars["EnemyHpLoss"].BaseValue,
            Owner.Creature,
            this);
    }

    protected override void OnUpgrade() => DynamicVars["EnemyHpLoss"].UpgradeValueBy(4);
}
