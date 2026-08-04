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

public sealed class BurningStrike : NinjaSlayerCardTemplate
{
    private static readonly NinjaSlayerCardSpec CardSpec = new(nameof(BurningStrike), 1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy, true);


    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(14, ValueProp.Move)
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromCard<BurningCard>()
    ];

    public BurningStrike() : base(CardSpec) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .WithDefectStrikeHitFx()
            .WithAttackerAnim("Attack", Owner.Character.AttackAnimDelay)
            .Targeting(cardPlay.Target!)
            .ExecuteWithFinisher(choiceContext, this, cardPlay);
        await NinjaSlayerActions.AddGeneratedCard<BurningCard>(Owner, PileType.Draw);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(4);
    }
}
