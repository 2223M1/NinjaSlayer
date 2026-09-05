using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using NinjaSlayer.Orbs;
using NinjaSlayer.Powers;
using STS2RitsuLib.Interop.AutoRegistration;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class TornadoFistRedesignV1 : RedesignV1UncommonCard
{
    protected override bool HasEnergyCostX => true;
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DamageVar(4, ValueProp.Move), new PowerVar<VulnerablePower>(1), new DynamicVar("Threshold", 4)];

    public TornadoFistRedesignV1()
        : base(nameof(TornadoFistRedesignV1), nameof(TornadoFistRedesignV1), 0, CardType.Attack, TargetType.AllEnemies) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.PangbaiLongjuanquanEvent);
        int hits = ResolveEnergyXValue();
        return this.ExecuteSequenceWithFinisher(
            choiceContext,
            cardPlay,
            hits,
            () => NinjaSlayerXAttackSequence.Run(
                Owner.Creature,
                hits,
                TornadoFistSpinAnimation.TurnSeconds,
                CombatActionTimingRuntime.AttackSeconds + CombatActionTimingRuntime.DamageRecoverySeconds,
                async _ =>
                {
                    AttackCommand command;
                    using (CombatPresentationPacingScope.Begin(CombatPresentationPacingPolicy.PreserveDamage))
                    {
                        command = await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
                            .FromCard(this)
#else
                            .FromCard(this, cardPlay)
#endif
                            .WithDefectStrikeHitFx()
                            .WithAttackerAnim(TornadoFistSpinAnimation.TriggerName, TornadoFistSpinAnimation.TurnSeconds)
                            .TargetingAllOpponents(CombatState!)
                            .Execute(choiceContext);
                    }

                    if (hits >= DynamicVars["Threshold"].IntValue)
                    {
                        foreach (Creature target in command.Results
                                     .SelectMany(results => results)
                                     .Select(result => result.Receiver)
                                     .Where(target => target.IsAlive && target.Side != Owner.Creature.Side)
                                     .Distinct())
                        {
                            await PowerCmd.Apply<VulnerablePower>(
                                choiceContext,
                                target,
                                DynamicVars.Vulnerable.BaseValue,
                                Owner.Creature,
                                this);
                        }
                    }

                    return CombatState!.HittableEnemies.Count == 0;
                }));
    }

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(2);
}
