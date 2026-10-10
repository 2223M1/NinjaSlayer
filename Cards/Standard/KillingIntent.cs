using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.Standard;

public sealed class KillingIntent : NinjaSlayerRareCard
{
    protected override bool ShouldGlowGoldInternal =>
        CombatState?.HittableEnemies.Any(enemy => enemy.Monster?.IntendsToAttack == true) == true;

    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new BlockVar(9, ValueProp.Move), new DamageVar(47, ValueProp.Move)];

    public KillingIntent()
        : base(nameof(KillingIntent), 2, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
        await PowerCmd.Apply<KillingIntentPower>(
            choiceContext,
            Owner.Creature,
            Math.Max(0, DynamicVars.Damage.BaseValue - Owner.Creature.GetPowerAmount<KillingIntentPower>()),
            Owner.Creature,
            this);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(3);
        DynamicVars.Damage.UpgradeValueBy(9);
    }
}
