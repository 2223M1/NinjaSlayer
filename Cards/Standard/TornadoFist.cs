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

namespace NinjaSlayer.Cards.Standard;

public sealed class TornadoFist : NinjaSlayerUncommonCard
{
    internal bool IsEmpowered(decimal x) => x >= DynamicVars["Threshold"].IntValue;
    internal bool ShouldCharge => CombatState != null
        && IsEmpowered(MegaCrit.Sts2.Core.Hooks.Hook.ModifyXValue(CombatState, this, Owner.PlayerCombatState!.Energy));
    protected override bool ShouldGlowGoldInternal => ShouldCharge;

    protected override bool HasEnergyCostX => true;
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DamageVar(4, ValueProp.Move), new DynamicVar("Threshold", 4)];

    public TornadoFist()
        : base(nameof(TornadoFist), 0, CardType.Attack, TargetType.AllEnemies) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        int x = ResolveEnergyXValue();
        int hits = IsEmpowered(x) ? x * 2 : x;
        if (hits <= 0)
        {
            NinjaSlayer.Code.Nodes.NinjaSlayerAimPose.Get(Owner.Creature)?.ReleaseEmptyTornado();
            return;
        }
        using var cadence = NinjaSlayerAttackExecution.EnterSequence(hits);
        using var suppression = XAttackAudioContext.Suppress();
        using var pacing = CombatPresentationPacingScope.Begin(CombatPresentationPacingPolicy.RapidCard);
        SpinComboAudio? spin = null;
        try
        {
            XAttackComboMovement.BeginCombo(Owner.Creature, TornadoFistSpinAnimation.TurnSeconds);
            float duration = TornadoSpinTiming.SecondsToFinalHit(hits, CombatActionTimingRuntime.CurrentSpeed);
            if (MegaCrit.Sts2.Core.TestSupport.TestMode.IsOff && TornadoSpinTiming.Select(hits, duration,
                NinjaSlayerFormState.GetPresentation(Owner.Creature).ForcePerHitComboAudio) != TornadoAudioMode.PerHit)
                spin = SpinComboAudio.Start(Owner.Creature);
            AttackCommand command = DamageCmd.Attack(DynamicVars.Damage.BaseValue)
                .WithHitCount(hits)
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
                .FromCard(this)
#else
                .FromCard(this, cardPlay)
#endif
                .WithDefectStrikeHitFx()
                .WithAttackerAnim(TornadoFistSpinAnimation.TriggerName, TornadoFistSpinAnimation.TurnSeconds)
                .TargetingAllOpponents(CombatState!);
            command.BeforeDamage(() =>
            {
                cadence.SetHit(command.Results.Count());
                if (spin == null && MegaCrit.Sts2.Core.TestSupport.TestMode.IsOff)
                    NinjaSlayerCombatAudioSet.Play(NinjaSlayerCombatAudioSet.For(Owner.Creature).FastAttack);
                else if (spin != null && !NinjaSlayerAttackExecution.NeedsDamageRecovery) spin.Finish();
                return Task.CompletedTask;
            });
            await command.ExecuteWithFinisher(choiceContext, this, cardPlay, hitCountOverride: hits);
        }
        finally
        {
            if (spin != null && Godot.GodotObject.IsInstanceValid(spin)) spin.Finish();
            await XAttackComboMovement.EndCombo(Owner.Creature);
        }
    }

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(2);
}
