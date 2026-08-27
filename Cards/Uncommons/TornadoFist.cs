using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

public sealed class TornadoFist : NinjaSlayerXAttackCard
{
    private static readonly NinjaSlayerCardSpec CardSpec = new(nameof(TornadoFist), 0, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy, true);


    protected override string AttackerAnimTrigger => TornadoFistSpinAnimation.TriggerName;
    protected override float XAttackHitDelay => TornadoFistSpinAnimation.TurnSeconds;
    protected override float XAttackAudioHitDuration =>
        CombatActionTimingRuntime.AttackSeconds
        + CombatActionTimingRuntime.DamageRecoverySeconds;

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(6, ValueProp.Move),
        new PowerVar<VulnerablePower>(1)
    ];

    public TornadoFist() : base(CardSpec) { }

    protected override async Task<bool> ExecuteXHit(
        PlayerChoiceContext choiceContext,
        CardPlay cardPlay,
        int hitIndex,
        int totalHits)
    {
        return await ExecuteXHitCore(choiceContext, cardPlay, XAttackHitDelay);
    }

    private async Task<bool> ExecuteXHitCore(
        PlayerChoiceContext choiceContext,
        CardPlay cardPlay,
        float attackerAnimDelay)
    {

        using (CombatPresentationPacingScope.Begin(CombatPresentationPacingPolicy.PreserveDamage))
        {
            var command = DamageCmd.Attack(DynamicVars.Damage.BaseValue)
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
                .FromCard(this)
#else
                .FromCard(this, cardPlay)
#endif
                .WithDefectStrikeHitFx()
                .WithAttackerAnim(AttackerAnimTrigger, attackerAnimDelay)
                .Targeting(cardPlay.Target!);
            await command.Execute(choiceContext);
            List<DamageResult> results = command.Results.SelectMany(r => r).ToList();
            if (results.Any(r => r.WasTargetKilled))
            {
                return true;
            }

            if (results.Any(r => r.UnblockedDamage > 0))
            {
                await PowerCmd.Apply<VulnerablePower>(
                    choiceContext,
                    cardPlay.Target!,
                    DynamicVars["VulnerablePower"].BaseValue,
                    Owner.Creature,
                    this);
            }

            return false;
        }
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(3);
    }
}
