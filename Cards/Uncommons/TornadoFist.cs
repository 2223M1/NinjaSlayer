using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

public sealed class TornadoFist : NinjaSlayerXAttackCard
{
    private const int energyCost = 0;
    private const CardType type = CardType.Attack;
    private const CardRarity rarity = CardRarity.Uncommon;
    private const TargetType targetType = TargetType.AnyEnemy;
    private const bool shouldShowInCardLibrary = true;

    protected override float XAttackHitDelay => 0f;

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(6, ValueProp.Move),
        new PowerVar<VulnerablePower>(1)
    ];

    public TornadoFist() : base(energyCost, type, rarity, targetType, shouldShowInCardLibrary) { }

    protected override Task OnBeforeXHit(
        PlayerChoiceContext choiceContext,
        CardPlay cardPlay,
        int hitIndex,
        int totalHits)
    {
        if (Owner.Creature.HasPower<NarakuPower>())
        {
            SpinComboAudio.PlayNarakuSlowAttack(Owner.Creature);
        }

        return Task.CompletedTask;
    }

    protected override async Task ExecuteXHit(
        PlayerChoiceContext choiceContext,
        CardPlay cardPlay,
        int hitIndex,
        int totalHits)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);

        var command = DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .WithDefectStrikeHitFx()
            .WithAttackerAnim(AttackerAnimTrigger, XAttackHitDelay)
            .Targeting(cardPlay.Target);
        await command.Execute(choiceContext);
        if (command.Results.SelectMany(r => r).Any(r => r.UnblockedDamage > 0))
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
        DynamicVars.Damage.UpgradeValueBy(3);
    }
}
