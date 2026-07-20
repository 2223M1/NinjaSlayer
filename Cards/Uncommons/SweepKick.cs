using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

public sealed class SweepKick : NinjaSlayerCardTemplate
{
    private const int energyCost = 2;
    private const CardType type = CardType.Attack;
    private const CardRarity rarity = CardRarity.Uncommon;
    private const TargetType targetType = TargetType.AllEnemies;
    private const bool shouldShowInCardLibrary = true;

    // ponytail: reuse karate art until this card gets dedicated art.
    public override CardAssetProfile AssetProfile => NinjaSlayerCardAssets.Named("KarateFinish");

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(7, ValueProp.Move),
        new KarateVar(2)
    ];

    public SweepKick() : base(energyCost, type, rarity, targetType, shouldShowInCardLibrary) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .WithDefectStrikeHitFx()
            .WithAttackerAnim("Attack", Owner.Character.AttackAnimDelay)
            .TargetingAllOpponents(CombatState ?? throw new InvalidOperationException("Sweep Kick requires combat."))
            .ExecuteWithFinisher(choiceContext, this, cardPlay);
        foreach (Creature enemy in CombatState?.HittableEnemies.ToList() ?? [])
        {
            await PowerCmd.Apply<KaratePower>(choiceContext, enemy, DynamicVars.Karate().BaseValue, Owner.Creature, this);
        }
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(2);
    }
}
