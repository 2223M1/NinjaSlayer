using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using NinjaSlayer.Orbs;
using NinjaSlayer.Powers;
using STS2RitsuLib.Interop.AutoRegistration;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class PalmThrustRedesignV1 : RedesignV1CommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DamageVar(5, ValueProp.Move), new RepeatVar(2)];

    public PalmThrustRedesignV1()
        : base(nameof(PalmThrustRedesignV1), "PalmThrust", 1, CardType.Attack, TargetType.RandomEnemy) { }

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
                    IReadOnlyList<Creature> enemies = CombatState!.HittableEnemies;
                    Creature? target = Owner.RunState.Rng.CombatTargets.NextItem(enemies);
                    if (target == null)
                    {
                        return true;
                    }

                    AttackCommand command = DamageCmd.Attack(DynamicVars.Damage.BaseValue)
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
                        .FromCard(this)
#else
                        .FromCard(this, cardPlay)
#endif
                        .WithDefectStrikeHitFx()
                        .WithAttackerAnim("Attack", Owner.Character.AttackAnimDelay)
                        .Targeting(target);
                    await command.Execute(choiceContext);
                    return CombatState.HittableEnemies.Count == 0;
                }));
    }

    protected override void OnUpgrade() => DynamicVars.Repeat.UpgradeValueBy(1);
}
