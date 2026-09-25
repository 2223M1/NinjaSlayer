using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class StormFistRedesignV1 : RedesignV1RareCard
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        HoverTipFactory.FromCardWithCardHoverTips<ChadoEnergyRedesignV1>();

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new CalculationBaseVar(4), new ExtraDamageVar(3),
        new CalculatedDamageVar(ValueProp.Move)
            .WithMultiplier((card, _) => PileType.Exhaust.GetPile(card.Owner).Cards.OfType<ChadoEnergyRedesignV1>().Count()),
        new RepeatVar(4)
    ];
    public StormFistRedesignV1()
        : base(nameof(StormFistRedesignV1), "TornadoFist", 3, CardType.Attack, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        int hits = DynamicVars.Repeat.IntValue;
        await this.ExecuteSequenceWithFinisher(choiceContext, cardPlay, hits,
            () => NinjaSlayerXAttackSequence.Run(Owner.Creature, hits,
                Owner.Character.AttackAnimDelay, Owner.Character.AttackAnimDelay, async _ =>
                {
                    await DamageCmd.Attack(DynamicVars.CalculatedDamage)
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
                        .FromCard(this)
#else
                        .FromCard(this, cardPlay)
#endif
                        .WithDefectStrikeHitFx()
                        .WithAttackerAnim("SlowAttack", Owner.Character.AttackAnimDelay)
                        .Targeting(cardPlay.Target!).Execute(choiceContext);
                    return !cardPlay.Target!.IsAlive;
                }));
    }

    protected override void OnUpgrade()
    {
        DynamicVars.ExtraDamage.UpgradeValueBy(1);
        DynamicVars.CalculationBase.UpgradeValueBy(2);
    }
}
