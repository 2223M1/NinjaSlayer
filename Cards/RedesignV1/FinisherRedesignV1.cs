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
            .WithMultiplier((card, _) => PileType.Exhaust.GetPile(card.Owner).Cards.Count(c => c is ChadoEnergyRedesignV1)),
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
