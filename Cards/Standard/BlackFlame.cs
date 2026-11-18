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
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using STS2RitsuLib.Interop.AutoRegistration;

namespace NinjaSlayer.Cards.Standard;

[RegisterCard(typeof(StatusCardPool))]
public sealed class BlackFlame : NinjaSlayerStandaloneCardTemplate
{
    // Native CardPlay identity survives nested autoplay and patches installed before our wrapper hooks.
    private static readonly ConditionalWeakTable<CardPlay, AttackBurn> AttackBurns = new();

    private static readonly NinjaSlayerCardSpec Spec = new(
        nameof(BlackFlame),
        -2,
        CardType.Status,
        CardRarity.Status,
        TargetType.Self,
        false,
        nameof(BlackFlame));

    public override bool CanBeGeneratedInCombat => false;
    public override bool CanBeGeneratedByModifiers => false;
    public override bool HasTurnEndInHandEffect => true;
    public override IEnumerable<CardKeyword> CanonicalKeywords =>
        [CardKeyword.Unplayable, CardKeyword.Ethereal];
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DamageVar(NinjaSlayerCardRules.BlackFlameDamage, ValueProp.Unblockable | ValueProp.Unpowered)];
    protected override IEnumerable<string> ExtraRunAssetPaths =>
        NNinjaSlayerGroundFireVfx.AssetPaths;

    public BlackFlame() : base(Spec) { }

    private bool IsHeldForAttack(CardPlay cardPlay) =>
        Pile?.Type == PileType.Hand
        && cardPlay.Card.Owner == Owner
        && cardPlay.Card.Type == CardType.Attack;

    public override Task BeforeCardPlayed(CardPlay cardPlay)
    {
        if (IsHeldForAttack(cardPlay))
            AttackBurns.GetValue(cardPlay, _ => new AttackBurn(
                PileType.Hand.GetPile(Owner).Cards.OfType<BlackFlame>().ToArray()));
        return Task.CompletedTask;
    }

    public override Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        IsHeldForAttack(cardPlay)
            ? TriggerFromAttack(choiceContext, cardPlay)
            : Task.CompletedTask;

    protected override async Task OnTurnEndInHand(PlayerChoiceContext choiceContext)
    {
        // Multiplayer keeps the room running after one player's death clears
        // their piles; its previously queued turn-end cards can still arrive here.
        if (!Owner.Creature.IsAlive || !CombatManager.Instance.IsInProgress || CombatManager.Instance.IsEnding) return;
        ICombatState combatState = CombatState
            ?? throw new InvalidOperationException("Black Flame requires combat.");
        List<Creature> enemies = combatState.Creatures
            .Where(creature => NinjaSlayerCardRules.IsBlackFlameTurnEndTarget(
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
                , null
            );
        }
    }

    private sealed class AttackBurn(BlackFlame[] flames)
    {
        public BlackFlame[] Flames { get; } = flames;
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
            await DamageEnemies(choiceContext, Owner, (int)flame.DynamicVars.Damage.BaseValue, flame, targets, fromAttack: true);
        }
    }

    private Task DamageEnemies(PlayerChoiceContext choiceContext, List<Creature> enemies) =>
        DamageEnemies(choiceContext, Owner, (int)DynamicVars.Damage.BaseValue, this, enemies, fromAttack: false);

    internal static async Task DamageEnemies(PlayerChoiceContext choiceContext, Player player,
        int baseDamage, CardModel? source, IReadOnlyList<Creature> targets, bool fromAttack)
    {
        if (targets.Count == 0) return;
        // Reward each flame activation once, including Naraku Form and turn-end flames.
        if (player.Creature.GetPower<RekindlePower>() is { } rekindle)
        {
            await PowerCmd.Apply<NarakuLifePower>(choiceContext, player.Creature,
                rekindle.Amount, player.Creature, source);
        }
        int amount = baseDamage + player.Creature.GetPowerAmount<BlackFlameInfernoPower>();
        const ValueProp props = ValueProp.Unblockable | ValueProp.Unpowered;
        // Karate and prior flames have already resolved. Only the actual lethal wave owns the pose.
        await using var finisher = fromAttack
            ? FinisherEligibilityService.CreateActionSession(player.Creature, new FinisherActionForecastDescriptor(
                _ => amount, props, 1, FinisherTargeting.Fixed, CardSource: source, TriggersKarate: false, FixedTargets: targets))
            : null;
        finisher?.Begin();
        var results = await CreatureCmd.Damage(choiceContext, targets, amount, props, player.Creature, source, null);
        Code.Telemetry.NinjaSlayerCombatTelemetry.AttributeDamage(results, "black_flame");
        if (finisher != null) await finisher.CompleteAsync(playPose: true);
    }

    protected override void OnUpgrade() { }
}
