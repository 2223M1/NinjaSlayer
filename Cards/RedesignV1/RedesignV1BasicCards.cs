using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class StrikeNinjaSlayerRedesignV1 : NinjaSlayerRedesignCardTemplate
{
    private static readonly NinjaSlayerCardSpec Spec = new(
        nameof(StrikeNinjaSlayerRedesignV1),
        1,
        CardType.Attack,
        CardRarity.Basic,
        TargetType.AnyEnemy,
        true,
        Tags: [CardTag.Strike]);

    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(6, ValueProp.Move)];

    public StrikeNinjaSlayerRedesignV1() : base(Spec, nameof(StrikeNinjaSlayer)) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .WithDefectStrikeHitFx()
            .WithAttackerAnim("Attack", Owner.Character.AttackAnimDelay)
            .Targeting(cardPlay.Target!)
            .ExecuteWithFinisher(choiceContext, this, cardPlay);

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(3);
}

public sealed class DefendNinjaSlayerRedesignV1 : NinjaSlayerRedesignCardTemplate
{
    private static readonly NinjaSlayerCardSpec Spec = new(
        nameof(DefendNinjaSlayerRedesignV1),
        1,
        CardType.Skill,
        CardRarity.Basic,
        TargetType.Self,
        true,
        Tags: [CardTag.Defend]);

    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(5, ValueProp.Move)];

    public DefendNinjaSlayerRedesignV1() : base(Spec, nameof(DefendNinjaSlayer)) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(3);
}

public sealed class KarateStraightRedesignV1 : NinjaSlayerRedesignCardTemplate
{
    private static readonly NinjaSlayerCardSpec Spec = new(
        nameof(KarateStraightRedesignV1),
        2,
        CardType.Attack,
        CardRarity.Basic,
        TargetType.AnyEnemy,
        true);

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DamageVar(10, ValueProp.Move), new KarateVar(3)];

    public KarateStraightRedesignV1() : base(Spec, nameof(KarateStraight)) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .WithHeavyBluntHitFx()
            .WithAttackerAnim("SlowAttack", Owner.Character.AttackAnimDelay)
            .Targeting(cardPlay.Target!)
            .ExecuteWithFinisher(choiceContext, this, cardPlay);
        await PowerCmd.Apply<KaratePower>(
            choiceContext,
            Owner.Creature,
            DynamicVars.Karate().BaseValue,
            Owner.Creature,
            this);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(4);
        DynamicVars.Karate().UpgradeValueBy(1);
    }
}

public sealed class TurtleShellRedesignV1 : NinjaSlayerRedesignCardTemplate
{
    private static readonly NinjaSlayerCardSpec Spec = new(
        nameof(TurtleShellRedesignV1),
        1,
        CardType.Skill,
        CardRarity.Basic,
        TargetType.Self,
        true);

    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("BonusPlating", 0)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromPower<KaratePower>(), HoverTipFactory.FromPower<PlatingPower>()];

    public TurtleShellRedesignV1() : base(Spec, "BlockCard") { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        KaratePower? karate = Owner.Creature.GetPower<KaratePower>();
        int plating = RedesignV1Rules.ResolveTurtleShellPlating(
            karate?.Amount ?? 0,
            DynamicVars["BonusPlating"].IntValue);
        if (karate != null)
        {
            await PowerCmd.Remove(karate);
        }

        if (plating > 0)
        {
            await PowerCmd.Apply<PlatingPower>(
                choiceContext,
                Owner.Creature,
                plating,
                Owner.Creature,
                this);
        }
    }

    protected override void OnUpgrade() => DynamicVars["BonusPlating"].UpgradeValueBy(2);
}

public interface IReturnToHandAfterPlay;
