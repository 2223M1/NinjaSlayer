using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using STS2RitsuLib.Interop.AutoRegistration;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class PunchRedesignV1 : NinjaSlayerStandaloneCardTemplate
{
    private static readonly NinjaSlayerCardSpec Spec = new(
        nameof(PunchRedesignV1),
        2,
        CardType.Attack,
        CardRarity.Token,
        TargetType.AnyEnemy,
        false,
        "ComboFist");

    public override bool CanBeGeneratedInCombat => false;
    public override bool CanBeGeneratedByModifiers => false;
    public override IEnumerable<CardKeyword> CanonicalKeywords =>
        [CardKeyword.Retain, CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DamageVar(4, ValueProp.Move), new RepeatVar(4), new DynamicVar("Growth", 2)];

    public PunchRedesignV1() : base(Spec) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        int hits = DynamicVars.Repeat.IntValue;
        return this.ExecuteSequenceWithFinisher(
            choiceContext,
            cardPlay,
            hits,
            () => NinjaSlayerXAttackSequence.Run(
                Owner.Creature,
                hits,
                Owner.Character.AttackAnimDelay,
                Owner.Character.AttackAnimDelay,
                async _ =>
                {
                    AttackCommand command = DamageCmd.Attack(DynamicVars.Damage.BaseValue)
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
                        .FromCard(this)
#else
                        .FromCard(this, cardPlay)
#endif
                        .WithHeavyBluntHitFx()
                        .WithAttackerAnim("SlowAttack", Owner.Character.AttackAnimDelay)
                        .Targeting(cardPlay.Target!);
                    await command.Execute(choiceContext);
                    return command.Results.SelectMany(result => result).Any(result => result.WasTargetKilled);
                }));
    }

    public override Task AfterFlush(
        PlayerChoiceContext choiceContext,
        Player player,
        IReadOnlyCollection<CardModel> flushedCards,
        IReadOnlyCollection<CardModel> retainedCards)
    {
        if (player == Owner && retainedCards.Contains(this))
        {
            DynamicVars.Damage.BaseValue += DynamicVars["Growth"].BaseValue;
        }

        return Task.CompletedTask;
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(2);
        DynamicVars["Growth"].UpgradeValueBy(1);
    }
}

[RegisterCard(typeof(TokenCardPool))]
public sealed class FinisherRedesignV1 : NinjaSlayerStandaloneCardTemplate
{
    private static readonly NinjaSlayerCardSpec Spec = new(
        nameof(FinisherRedesignV1),
        2,
        CardType.Attack,
        CardRarity.Token,
        TargetType.AnyEnemy,
        false,
        "ComboFist");

    public override bool CanBeGeneratedInCombat => false;
    public override bool CanBeGeneratedByModifiers => false;
    public override IEnumerable<CardKeyword> CanonicalKeywords =>
        [CardKeyword.Retain, CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new CalculationBaseVar(4),
        new ExtraDamageVar(4),
        new CalculatedDamageVar(ValueProp.Move)
            .WithMultiplier(NinjaSlayerActions.RedesignChadoInExhaustPileMultiplier),
        new RepeatVar(4)
    ];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromCard<ChadoEnergyRedesignV1>()];

    public FinisherRedesignV1() : base(Spec) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        int hits = DynamicVars.Repeat.IntValue;
        return this.ExecuteSequenceWithFinisher(
            choiceContext,
            cardPlay,
            hits,
            () => NinjaSlayerXAttackSequence.Run(
                Owner.Creature,
                hits,
                Owner.Character.AttackAnimDelay,
                Owner.Character.AttackAnimDelay,
                async _ =>
                {
                    AttackCommand command = DamageCmd.Attack(DynamicVars.CalculatedDamage)
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
                        .FromCard(this)
#else
                        .FromCard(this, cardPlay)
#endif
                        .WithHeavyBluntHitFx()
                        .WithAttackerAnim("SlowAttack", Owner.Character.AttackAnimDelay)
                        .Targeting(cardPlay.Target!);
                    await command.Execute(choiceContext);
                    return !cardPlay.Target!.IsAlive;
                }));
    }

    protected override void OnUpgrade()
    {
        DynamicVars.CalculationBase.UpgradeValueBy(2);
        DynamicVars.ExtraDamage.UpgradeValueBy(2);
    }
}

public sealed class IyaEchoRedesignV1 : NinjaSlayerStandaloneCardTemplate
{
    private static readonly NinjaSlayerCardSpec Spec = new(
        nameof(IyaEchoRedesignV1),
        1,
        CardType.Attack,
        CardRarity.Token,
        TargetType.AnyEnemy,
        false,
        nameof(IHit));

    public override bool CanBeGeneratedInCombat => false;
    public override bool CanBeGeneratedByModifiers => false;
    public override IEnumerable<CardKeyword> CanonicalKeywords =>
        [CardKeyword.Ethereal, CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(9, ValueProp.Move)];

    public IyaEchoRedesignV1() : base(Spec) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
            .FromCard(this)
#else
            .FromCard(this, cardPlay)
#endif
            .WithDefectStrikeHitFx()
            .WithAttackerAnim("Attack", Owner.Character.AttackAnimDelay)
            .Targeting(cardPlay.Target!)
            .ExecuteWithFinisher(choiceContext, this, cardPlay);

        IyaEchoRedesignV1 echo = CombatState!.CreateCard<IyaEchoRedesignV1>(Owner);
        if (IsUpgraded)
        {
            CardCmd.Upgrade(echo);
        }

        await CardPileCmd.AddGeneratedCardToCombat(echo, PileType.Hand, Owner);
    }

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(4);
}

[RegisterCard(typeof(StatusCardPool))]
public sealed class BlackFlameRedesignV1 : NinjaSlayerStandaloneCardTemplate
{
    private static readonly NinjaSlayerCardSpec Spec = new(
        nameof(BlackFlameRedesignV1),
        -2,
        CardType.Status,
        CardRarity.Status,
        TargetType.Self,
        false,
        nameof(BurningCard));

    public override bool CanBeGeneratedInCombat => false;
    public override bool CanBeGeneratedByModifiers => false;
    public override bool HasTurnEndInHandEffect => true;
    public override IEnumerable<CardKeyword> CanonicalKeywords =>
        [CardKeyword.Unplayable, CardKeyword.Ethereal];
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DamageVar(RedesignV1Rules.BlackFlameDamage, ValueProp.Unblockable | ValueProp.Unpowered)];
    protected override IEnumerable<string> ExtraRunAssetPaths =>
        NNinjaSlayerGroundFireVfx.AssetPaths;

    public BlackFlameRedesignV1() : base(Spec) { }

    public override Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        Pile?.Type == PileType.Hand
        && cardPlay.Card.Owner == Owner
        && cardPlay.Card.Type == CardType.Attack
            ? DamageEnemies(choiceContext)
            : Task.CompletedTask;

    protected override async Task OnTurnEndInHand(PlayerChoiceContext choiceContext)
    {
        ICombatState combatState = CombatState
            ?? throw new InvalidOperationException("Black Flame requires combat.");
        List<Creature> enemies = combatState.Creatures
            .Where(creature => RedesignV1Rules.IsBlackFlameTurnEndTarget(
                creature.IsAlive,
                false,
                creature.Side == Owner.Creature.Side))
            .ToList();
        NinjaSlayerCombatVfx.PlayBurnStatusFeedback(enemies.Prepend(Owner.Creature));
        await DamageEnemies(choiceContext, enemies);
        if (Owner.Creature.IsAlive)
        {
            await CreatureCmd.Damage(
                choiceContext,
                [Owner.Creature],
                DynamicVars.Damage.BaseValue,
                DynamicVars.Damage.Props,
                Owner.Creature,
                this
#if !NINJASLAYER_LEGACY_DAMAGE_API
                , null
#endif
            );
        }

        await CardCmd.Exhaust(choiceContext, this, causedByEthereal: true);
    }

    private Task DamageEnemies(PlayerChoiceContext choiceContext)
    {
        ICombatState combatState = CombatState
            ?? throw new InvalidOperationException("Black Flame requires combat.");
        List<Creature> enemies = combatState.Creatures
            .Where(creature => creature.IsAlive && creature.Side != Owner.Creature.Side)
            .ToList();
        NinjaSlayerCombatVfx.PlayBurnStatusFeedback(enemies);
        return DamageEnemies(choiceContext, enemies);
    }

    private Task DamageEnemies(PlayerChoiceContext choiceContext, IReadOnlyList<Creature> enemies)
    {
        if (enemies.Count == 0)
        {
            return Task.CompletedTask;
        }

        int damage = (int)DynamicVars.Damage.BaseValue
            + Owner.Creature.GetPowerAmount<BurnBurnBurnPower>();
        return CreatureCmd.Damage(
            choiceContext,
            enemies,
            damage,
            DynamicVars.Damage.Props,
            Owner.Creature,
            this
#if !NINJASLAYER_LEGACY_DAMAGE_API
            , null
#endif
        );
    }

    protected override void OnUpgrade() { }
}
