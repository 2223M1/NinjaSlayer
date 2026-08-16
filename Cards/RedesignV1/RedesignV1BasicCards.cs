using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class StrikeNinjaSlayerRedesignV1 : NinjaSlayerRedesignCardTemplate
{
    private static readonly NinjaSlayerCardSpec Spec = new(
        nameof(StrikeNinjaSlayerRedesignV1), 1, CardType.Attack, CardRarity.Basic, TargetType.AnyEnemy, true,
        Tags: [CardTag.Strike]);

    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(6, ValueProp.Move)];

    public StrikeNinjaSlayerRedesignV1() : base(Spec, nameof(StrikeNinjaSlayer)) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .WithAttackerAnim("Attack", Owner.Character.AttackAnimDelay)
            .Targeting(cardPlay.Target!)
            .Execute(choiceContext);

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(3);
}

public sealed class DefendNinjaSlayerRedesignV1 : NinjaSlayerRedesignCardTemplate
{
    private static readonly NinjaSlayerCardSpec Spec = new(
        nameof(DefendNinjaSlayerRedesignV1), 1, CardType.Skill, CardRarity.Basic, TargetType.Self, true,
        Tags: [CardTag.Defend]);

    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(5, ValueProp.Move)];

    public DefendNinjaSlayerRedesignV1() : base(Spec, nameof(DefendNinjaSlayer)) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        MegaCrit.Sts2.Core.Commands.CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(3);
}

public sealed class HandChopRedesignV1 : NinjaSlayerRedesignCardTemplate, IReturnToHandAfterPlay
{
    private static readonly NinjaSlayerCardSpec Spec = new(
        nameof(HandChopRedesignV1), 1, CardType.Attack, CardRarity.Basic, TargetType.AnyEnemy, true);

    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(7, ValueProp.Move)];

    public HandChopRedesignV1() : base(Spec, nameof(Chop)) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .WithAttackerAnim("Attack", Owner.Character.AttackAnimDelay)
            .Targeting(cardPlay.Target!)
            .Execute(choiceContext);

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(4);
}

public interface IReturnToHandAfterPlay;
