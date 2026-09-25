using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.ValueProps;
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
        nameof(BlackFlameRedesignV1));

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

    private bool IsHeldForAttack(CardPlay cardPlay) =>
        Pile?.Type == PileType.Hand
        && cardPlay.Card.Owner == Owner
        && cardPlay.Card.Type == CardType.Attack;

    public override Task BeforeCardPlayed(CardPlay cardPlay)
    {
        if (IsHeldForAttack(cardPlay))
            AttackBurns.GetValue(cardPlay, _ => new AttackBurn(
                PileType.Hand.GetPile(Owner).Cards.OfType<BlackFlameRedesignV1>().ToArray()));
        return Task.CompletedTask;
    }

    public override Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        IsHeldForAttack(cardPlay)
            ? TriggerFromAttack(choiceContext, cardPlay)
            : Task.CompletedTask;

    protected override async Task OnTurnEndInHand(PlayerChoiceContext choiceContext)
    {
        if (!CombatManager.Instance.IsInProgress || CombatManager.Instance.IsEnding) return;
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
    }

    private sealed class AttackBurn(BlackFlameRedesignV1[] flames)
    {
        public BlackFlameRedesignV1[] Flames { get; } = flames;
        public bool Resolved;
    }

    private async Task TriggerFromAttack(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // Cards generated during this attack did not observe its BeforeCardPlayed hook.
        if (!AttackBurns.TryGetValue(cardPlay, out AttackBurn? burn) || burn.Resolved)
            return;
        burn.Resolved = true;
        foreach (var flame in burn.Flames)
        {
            if (CombatManager.Instance.IsOverOrEnding || !Owner.Creature.IsAlive) break;
            if (flame.Pile?.Type != PileType.Hand || flame.Owner != Owner) continue;
            var targets = CombatState!.HittableEnemies;
            if (targets.Count == 0) break;
            if (LocalContext.IsMine(flame)
                && MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance?.Ui.Hand.GetCardHolder(flame) is NHandCardHolder holder)
                holder.Flash();
            NinjaSlayerCombatVfx.PlayBurnStatusFeedback(targets);
            await DamageEnemies(choiceContext, Owner, (int)flame.DynamicVars.Damage.BaseValue, flame, targets);
        }
    }

    private Task DamageEnemies(PlayerChoiceContext choiceContext, List<Creature> enemies) =>
        DamageEnemies(choiceContext, Owner, (int)DynamicVars.Damage.BaseValue, this, enemies);

    internal static async Task DamageEnemies(PlayerChoiceContext choiceContext, Player player,
        int baseDamage, CardModel? source, IReadOnlyList<Creature>? targets = null)
    {
        targets ??= player.Creature.CombatState!.HittableEnemies;
        if (targets.Count == 0) return;
        var results = await CreatureCmd.Damage(choiceContext, targets,
            baseDamage + player.Creature.GetPowerAmount<BurnBurnBurnPower>(),
            ValueProp.Unblockable | ValueProp.Unpowered, player.Creature, source
#if !NINJASLAYER_LEGACY_DAMAGE_API
            , null
#endif
        );
        Code.Telemetry.NinjaSlayerCombatTelemetry.AttributeDamage(results, "black_flame");
    }

    protected override void OnUpgrade() { }
}
