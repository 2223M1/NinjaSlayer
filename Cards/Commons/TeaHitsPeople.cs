using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

public sealed class TeaHitsPeople : NinjaSlayerCardTemplate
{
    public override CardAssetProfile AssetProfile => NinjaSlayerCardAssets.Named("ChadoCard");

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(6, ValueProp.Move)
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromCard<ChadoCard>()
    ];

    protected override bool ShouldGlowGoldInternal => NinjaSlayerActions.ChadoExhaustedThisTurn(this);

    public TeaHitsPeople() : base(1, CardType.Attack, CardRarity.Common, TargetType.AllEnemies, true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        int hitCount = NinjaSlayerActions.ChadoExhaustedThisTurn(this) ? 2 : 1;
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .WithHitCount(hitCount)
            .FromCard(this, cardPlay)
            .WithDefectStrikeHitFx()
            .WithAttackerAnim("Attack", Owner.Character.AttackAnimDelay)
            .TargetingAllOpponents(CombatState ?? throw new InvalidOperationException("Tea Hits People requires combat."))
            .Execute(choiceContext);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(2);
    }
}
