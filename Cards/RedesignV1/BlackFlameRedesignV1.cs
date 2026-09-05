using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using STS2RitsuLib.Interop.AutoRegistration;

namespace NinjaSlayer.Cards.RedesignV1;

[RegisterCard(typeof(StatusCardPool))]
public sealed class BlackFlameRedesignV1 : NinjaSlayerStandaloneCardTemplate
{
    private static readonly NinjaSlayerCardSpec Spec = new(
        nameof(BlackFlameRedesignV1),
        -2,
        CardType.Status,
        CardRarity.Status,
        TargetType.Self,
        false,
        "BurningCard");

    public override bool CanBeGeneratedInCombat => false;
    public override bool CanBeGeneratedByModifiers => false;
    public override bool HasTurnEndInHandEffect => true;
    public override IEnumerable<CardKeyword> CanonicalKeywords =>
        [CardKeyword.Unplayable, CardKeyword.Ethereal];
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DamageVar(RedesignV1Rules.BlackFlameDamage, ValueProp.Unblockable | ValueProp.Unpowered)];
    protected override IEnumerable<string> ExtraRunAssetPaths =>
        NNinjaSlayerGroundFireVfx.AssetPaths;

    public BlackFlameRedesignV1() : base(Spec) { }

    public override Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        Pile?.Type == PileType.Hand
        && cardPlay.Card.Owner == Owner
        && cardPlay.Card.Type == CardType.Attack
            ? DamageEnemies(choiceContext)
            : Task.CompletedTask;

    protected override async Task OnTurnEndInHand(PlayerChoiceContext choiceContext)
    {
        ICombatState combatState = CombatState
            ?? throw new InvalidOperationException("Black Flame requires combat.");
        List<Creature> enemies = combatState.Creatures
            .Where(creature => RedesignV1Rules.IsBlackFlameTurnEndTarget(
                creature.IsAlive,
                false,
                creature.Side == Owner.Creature.Side))
            .ToList();
        NinjaSlayerCombatVfx.PlayBurnStatusFeedback(enemies.Prepend(Owner.Creature));
        await DamageEnemies(choiceContext, enemies);
        if (Owner.Creature.IsAlive)
        {
            await CreatureCmd.Damage(
                choiceContext,
                [Owner.Creature],
                DynamicVars.Damage.BaseValue,
                DynamicVars.Damage.Props,
                Owner.Creature,
                this
#if !NINJASLAYER_LEGACY_DAMAGE_API
                , null
#endif
            );
        }

        await CardCmd.Exhaust(choiceContext, this, causedByEthereal: true);
    }

    private Task DamageEnemies(PlayerChoiceContext choiceContext)
    {
        ICombatState combatState = CombatState
            ?? throw new InvalidOperationException("Black Flame requires combat.");
        List<Creature> enemies = combatState.Creatures
            .Where(creature => creature.IsAlive && creature.Side != Owner.Creature.Side)
            .ToList();
        NinjaSlayerCombatVfx.PlayBurnStatusFeedback(enemies);
        return DamageEnemies(choiceContext, enemies);
    }

    private Task DamageEnemies(PlayerChoiceContext choiceContext, List<Creature> enemies)
    {
        if (enemies.Count == 0)
        {
            return Task.CompletedTask;
        }

        int damage = (int)DynamicVars.Damage.BaseValue
            + Owner.Creature.GetPowerAmount<BurnBurnBurnPower>();
        return CreatureCmd.Damage(
            choiceContext,
            enemies,
            damage,
            DynamicVars.Damage.Props,
            Owner.Creature,
            this
#if !NINJASLAYER_LEGACY_DAMAGE_API
            , null
#endif
        );
    }

    protected override void OnUpgrade() { }
}
