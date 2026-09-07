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
    private IEnumerable<CardModel> AvailableChado => new[] { PileType.Draw, PileType.Hand, PileType.Discard }
        .SelectMany(pile => pile.GetPile(Owner).Cards).OfType<ChadoEnergyRedesignV1>();
    protected override bool IsPlayable => AvailableChado.Count() >= 3;
    protected override bool ShouldGlowGoldInternal => IsPlayable;
    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new CalculationBaseVar(4), new ExtraDamageVar(6),
        new CalculatedDamageVar(ValueProp.Move)
            .WithMultiplier((card, _) => PileType.Exhaust.GetPile(card.Owner).Cards.OfType<ChadoEnergyRedesignV1>().Count()),
        new RepeatVar(4)
    ];
    public StormFistRedesignV1()
        : base(nameof(StormFistRedesignV1), "TornadoFist", 4, CardType.Attack, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        foreach (CardModel card in AvailableChado.ToArray())
            await CardCmd.Exhaust(choiceContext, card);
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
                        .WithHeavyBluntHitFx()
                        .WithAttackerAnim("SlowAttack", Owner.Character.AttackAnimDelay)
                        .Targeting(cardPlay.Target!).Execute(choiceContext);
                    return !cardPlay.Target!.IsAlive;
                }));
    }

    protected override void OnUpgrade()
    {
        EnergyCost.UpgradeBy(-1);
        DynamicVars.CalculationBase.UpgradeValueBy(2);
    }
}
