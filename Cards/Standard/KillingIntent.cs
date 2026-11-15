using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.Standard;

public sealed class KillingIntent : NinjaSlayerRareCard
{
    protected override bool ShouldGlowGoldInternal =>
        CombatState?.HittableEnemies.Any(enemy => enemy.Monster?.IntendsToAttack == true) == true;

    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(10, ValueProp.Move)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        HoverTipFactory.FromCardWithCardHoverTips<StraightKi>(IsUpgraded);

    public KillingIntent()
        : base(nameof(KillingIntent), 2, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
        var power = await PowerCmd.Apply<KillingIntentPower>(
            choiceContext,
            Owner.Creature,
            1,
            Owner.Creature,
            this);
        power?.QueueReward(IsUpgraded);
    }

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(2);
}
