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

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class LuckyStrikeRedesignV1 : RedesignV1CommonCard
{
    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new CardsVar(1),
        new BlockVar(2, ValueProp.Move),
        new DamageVar(3, ValueProp.Move)
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
    [
        HoverTipFactory.FromKeyword(NinjaSlayerKeywords.Scry)
    ];

    public LuckyStrikeRedesignV1()
        : base(nameof(LuckyStrikeRedesignV1), nameof(LuckyStrike), 0, CardType.Attack, TargetType.AnyEnemy)
    {
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
        await ScryCmd.Execute(choiceContext, Owner, DynamicVars.Cards.IntValue);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .WithDefectStrikeHitFx()
            .WithAttackerAnim("Attack", Owner.Character.AttackAnimDelay)
            .Targeting(cardPlay.Target!)
            .ExecuteWithFinisher(choiceContext, this, cardPlay);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Cards.UpgradeValueBy(1);
        DynamicVars.Block.UpgradeValueBy(1);
        DynamicVars.Damage.UpgradeValueBy(1);
    }
}

public sealed class PalmThrustRedesignV1 : RedesignV1CommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(6, ValueProp.Move),
        new RepeatVar(2)
    ];

    public PalmThrustRedesignV1()
        : base(nameof(PalmThrustRedesignV1), nameof(PalmThrust), 1, CardType.Attack, TargetType.RandomEnemy)
    {
    }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .WithHitCount(DynamicVars.Repeat.IntValue)
            .FromCard(this, cardPlay)
            .WithDefectStrikeHitFx()
            .WithAttackerAnim("Attack", Owner.Character.AttackAnimDelay)
            .TargetingRandomOpponents(CombatState ?? throw new InvalidOperationException("Palm Thrust requires combat."))
            .ExecuteWithFinisher(choiceContext, this, cardPlay);

    protected override void OnUpgrade() => DynamicVars.Repeat.UpgradeValueBy(1);
}

public sealed class NinjaWhipRedesignV1 : RedesignV1CommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(8, ValueProp.Move),
        new PowerVar<VulnerablePower>(1)
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
    [
        HoverTipFactory.FromPower<VulnerablePower>()
    ];

    protected override bool ShouldGlowGoldInternal =>
        NinjaSlayerCombatMetrics.PreviousFinishedCardWasAttack(Owner);

    public NinjaWhipRedesignV1()
        : base(nameof(NinjaWhipRedesignV1), nameof(NinjaWhip), 1, CardType.Attack, TargetType.AnyEnemy)
    {
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        bool shouldApplyVulnerable = NinjaSlayerCombatMetrics.PreviousFinishedCardWasAttack(Owner);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .WithDefectStrikeHitFx()
            .WithAttackerAnim("Attack", Owner.Character.AttackAnimDelay)
            .Targeting(cardPlay.Target!)
            .ExecuteWithFinisher(choiceContext, this, cardPlay);

        if (shouldApplyVulnerable && cardPlay.Target!.IsAlive)
        {
            await PowerCmd.Apply<VulnerablePower>(
                choiceContext,
                cardPlay.Target,
                DynamicVars["VulnerablePower"].BaseValue,
                Owner.Creature,
                this);
        }
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(2);
        DynamicVars["VulnerablePower"].UpgradeValueBy(1);
    }
}

public sealed class BurningStrikeRedesignV1 : RedesignV1CommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(14, ValueProp.Move)
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
    [
        HoverTipFactory.FromCard<BurningCard>()
    ];

    public BurningStrikeRedesignV1()
        : base(nameof(BurningStrikeRedesignV1), nameof(BurningStrike), 1, CardType.Attack, TargetType.AnyEnemy)
    {
    }

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

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(4);
}
