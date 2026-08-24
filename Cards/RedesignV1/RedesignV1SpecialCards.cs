using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;

namespace NinjaSlayer.Cards.RedesignV1;

[RegisterCard(typeof(TokenCardPool))]
public sealed class PunchRedesignV1 : NinjaSlayerStandaloneCardTemplate
{
    private static readonly NinjaSlayerCardSpec Spec = new(
        nameof(PunchRedesignV1),
        0,
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
        [new DamageVar(4, ValueProp.Move), new RepeatVar(4), new DynamicVar("Growth", 3)];

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
                        .FromCard(this, cardPlay)
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
        DynamicVars.Damage.UpgradeValueBy(1);
        DynamicVars["Growth"].UpgradeValueBy(1);
    }
}

[RegisterCard(typeof(TokenCardPool))]
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
            .FromCard(this, cardPlay)
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
        "BlackFlame");

    public override bool CanBeGeneratedInCombat => false;
    public override bool CanBeGeneratedByModifiers => false;
    public override bool HasTurnEndInHandEffect => true;
    public override IEnumerable<CardKeyword> CanonicalKeywords =>
        [CardKeyword.Unplayable, CardKeyword.Ethereal];
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new HpLossVar(2), new DamageVar(6, ValueProp.Unpowered | ValueProp.Move)];

    public BlackFlameRedesignV1() : base(Spec) { }

    protected override async Task OnTurnEndInHand(PlayerChoiceContext choiceContext)
    {
        if (Pile?.Type != PileType.Hand)
        {
            return;
        }

        List<CardModel> statuses = PileType.Hand.GetPile(Owner).Cards
            .Where(card => card.Type == CardType.Status)
            .ToList();
        foreach (CardModel status in statuses)
        {
            await CardCmd.Exhaust(choiceContext, status, causedByEthereal: status == this);
            NinjaSlayerCombatVfx.PlayBurnStatusFeedback([Owner.Creature]);
            await GameCompatibility.Damage.Deal(
                choiceContext,
                [Owner.Creature],
                DynamicVars.HpLoss.BaseValue,
                ValueProp.Unblockable | ValueProp.Unpowered,
                Owner.Creature,
                this,
                null);

            IReadOnlyList<Creature> enemies = CombatState?.HittableEnemies ?? [];
            if (enemies.Count > 0)
            {
                NinjaSlayerCombatVfx.PlayBurnStatusFeedback(enemies);
                await CreatureCmd.Damage(
                    choiceContext,
                    enemies,
                    DynamicVars.Damage.BaseValue,
                    DynamicVars.Damage.Props,
                    Owner.Creature);
            }
        }
    }

    protected override void OnUpgrade() { }
}
