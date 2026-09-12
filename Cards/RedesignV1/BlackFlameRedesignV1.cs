using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
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
    // Native CardPlay identity survives nested autoplay and patches installed before our wrapper hooks.
    private static readonly ConditionalWeakTable<CardPlay, AttackBurn> AttackBurns = new();

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
            ? TriggerFromAttack(choiceContext, cardPlay)
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
        if (Owner.Creature.IsAlive && !Owner.Creature.HasPower<OneBodyOneSoulPower>())
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
    }

    private sealed class AttackBurn
    {
        public bool Resolved;
    }

    private Task TriggerFromAttack(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (CombatState!.HittableEnemies.Count == 0) return Task.CompletedTask;
        AttackBurn burn = AttackBurns.GetOrCreateValue(cardPlay);
        if (burn.Resolved) return Task.CompletedTask;
        burn.Resolved = true;
        var flames = PileType.Hand.GetPile(Owner).Cards.OfType<BlackFlameRedesignV1>().ToArray();
        foreach (var flame in flames)
            if (LocalContext.IsMine(flame)
                && MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance?.Ui.Hand.GetCardHolder(flame) is NHandCardHolder holder)
                holder.Flash();
        return DamageEnemies(choiceContext, Owner, flames.Sum(flame => (int)flame.DynamicVars.Damage.BaseValue), this);
    }

    private Task DamageEnemies(PlayerChoiceContext choiceContext, List<Creature> enemies) =>
        DamageEnemies(choiceContext, Owner, (int)DynamicVars.Damage.BaseValue, this, enemies);

    internal static Task DamageEnemies(PlayerChoiceContext choiceContext, Player player,
        int baseDamage, CardModel? source, IReadOnlyList<Creature>? targets = null)
    {
        targets ??= player.Creature.CombatState!.HittableEnemies;
        if (targets.Count == 0) return Task.CompletedTask;
        NinjaSlayerCombatVfx.PlayBurnStatusFeedback(targets);
        return CreatureCmd.Damage(choiceContext, targets,
            baseDamage + player.Creature.GetPowerAmount<BurnBurnBurnPower>(),
            ValueProp.Unblockable | ValueProp.Unpowered, player.Creature, source
#if !NINJASLAYER_LEGACY_DAMAGE_API
            , null
#endif
        );
    }

    protected override void OnUpgrade() { }
}
