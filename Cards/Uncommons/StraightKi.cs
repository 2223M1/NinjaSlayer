using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

[RegisterCard(typeof(NinjaSlayerCardPool))]
public sealed class StraightKi : ModCardTemplate
{
    private const int energyCost = 2;
    private const CardType type = CardType.Attack;
    private const CardRarity rarity = CardRarity.Uncommon;
    private const TargetType targetType = TargetType.AnyEnemy;
    private const bool shouldShowInCardLibrary = true;

    protected override bool ShouldGlowGoldInternal =>
        CombatState?.HittableEnemies.Any(e => e.HasPower<WeakPower>() && e.HasPower<VulnerablePower>()) ?? false;

    public override CardAssetProfile AssetProfile => NinjaSlayerCardAssets.For(this);

    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Exhaust
    ];

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(18, ValueProp.Move),
        new DynamicVar("Karate", 4)
    ];

    public StraightKi() : base(energyCost, type, rarity, targetType, shouldShowInCardLibrary) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);
        bool shouldStun = cardPlay.Target.HasPower<WeakPower>() && cardPlay.Target.HasPower<VulnerablePower>();

        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .WithHeavyBluntHitFx()
            .WithAttackerAnim("SlowAttack", Owner.Character.AttackAnimDelay)
            .Targeting(cardPlay.Target)
            .Execute(choiceContext);

        await PowerCmd.Apply<KaratePower>(choiceContext, cardPlay.Target, DynamicVars["Karate"].BaseValue, Owner.Creature, this);

        if (shouldStun)
        {
            await CreatureCmd.Stun(cardPlay.Target);
        }
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(6);
    }
}
