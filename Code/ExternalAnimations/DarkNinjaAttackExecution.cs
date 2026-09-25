using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Content;
using NinjaSlayer.Monsters;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Code.ExternalAnimations;

internal readonly record struct DarkStrikeImpactOutcome(
    bool Connected,
    bool FullyBlocked,
    int Healing,
    bool ShouldContinue,
    bool HpLost = false);

internal static class DarkNinjaAttackExecution
{
    private const string DeathSlashImpactSfx =
        "event:/sfx/enemy/enemy_attacks/vantom/vantom_dismember";

    internal static async Task<AttackCommand> PlayDeathSlash(
        DarkNinjaMonster monster,
        IReadOnlyList<Creature> targets,
        int damage)
    {
        using var ranged = FinisherRangedAction.Begin(monster.Creature);
        Execution execution = await Execute(
            monster,
            targets,
            damage,
            DarkNinjaCombatMath.DeathSlashTotalSeconds,
            async (execution, targets) =>
            {
                await DarkNinjaSpecialAttackPresentation.PlayDeathSlash(
                    monster.Creature,
                    async () =>
                    {
                        Creature[] impactTargets = targets.Where(execution.CanHit).ToArray();
                        if (impactTargets.Length > 0)
                        {
                            Creature[] connectedTargets = impactTargets
                                .Where(execution.WillConnect)
                                .ToArray();
                            if (connectedTargets.Length > 0)
                            {
                                VfxCmd.PlayOnCreatureCenters(
                                    connectedTargets,
                                    VfxCmd.giantHorizontalSlashPath);
                                NinjaSlayerCombatAudioSet.Play(DeathSlashImpactSfx);
                            }

                            await execution.Deal(impactTargets);
                        }
                    });
            });
        return execution.Command;
    }

    internal static async Task PlayDarkStrike(
        DarkNinjaMonster monster,
        IReadOnlyList<Creature> targets,
        int damage,
        int returnSide = 1)
    {
        await Execute(
            monster,
            targets,
            damage,
            DarkNinjaCombatMath.GetDarkStrikeSegment(0).MotionSeconds,
            async (execution, targets) =>
            {
                await DarkNinjaSpecialAttackPresentation.PlayDarkStrike(
                    monster.Creature,
                    targets,
                    execution.CanHit,
                    async target =>
                    {
                        // Native player death removes combat piles during the damage command.
                        var player = target.Player ?? target.PetOwner;
                        CardModel[] candidates = player == null ? []
                            : CardPile.GetCards(player, PileType.Draw, PileType.Discard)
                                .Where(card => card.DeckVersion != null).ToArray();
                        NarakuLifePower? life = target.GetPower<NarakuLifePower>();
                        long absorbedBefore = life?.TotalAbsorbed ?? 0;
                        var previous = monster.DarkStrikeDamageConfirmed;
                        bool stolen = false;
                        monster.DarkStrikeDamageConfirmed = async (receiver, result) =>
                        {
                            if (stolen || receiver != target || result.Receiver != target
                                || (result.UnblockedDamage <= 0 && (life?.TotalAbsorbed ?? 0) <= absorbedBefore))
                                return;
                            stolen = true;
                            await monster.StealFrom(candidates);
                        };
                        try
                        {
                            DarkStrikeImpactOutcome outcome = await execution.Deal(target);
                            // Lethal retaliation detaches the attacker before its damage hook can run.
                            if (!stolen && (outcome.HpLost || (life?.TotalAbsorbed ?? 0) > absorbedBefore))
                            {
                                stolen = true;
                                await monster.StealFrom(candidates);
                            }
                            return outcome;
                        }
                        finally { monster.DarkStrikeDamageConfirmed = previous; }
                    }, returnSide: returnSide, canPenetrate: execution.WillPenetrate);
            });
    }

    private static async Task<Execution> Execute(
        DarkNinjaMonster monster,
        IReadOnlyList<Creature> targets,
        int damage,
        float hitSeconds,
        Func<Execution, IReadOnlyList<Creature>, Task> playPresentation)
    {
        Creature attacker = monster.Creature;
        AttackCommand command = DamageCmd.Attack(damage).FromMonster(monster);
        ICombatState? combatState = attacker.CombatState;
        var choiceContext = new BlockingPlayerChoiceContext();
        var execution = new Execution(
            command,
            choiceContext,
            attacker,
            combatState,
            damage);
        if (combatState is null || !execution.CanContinue())
        {
            return execution;
        }

        Creature[] pendingTargets = targets.ToArray();

        await Hook.BeforeAttack(combatState, command);
        FinisherApproach? approach = null;
        if (FinisherRangedAction.For(attacker) == null
            && FinisherAttackCommandAdapter.PredictReverseVictim(command, pendingTargets, damage, 1)
            ?.GetCreatureNode() is { } focus && attacker.GetCreatureNode() is { } actorNode)
        {
            approach = FinisherApproach.Create(actorNode, focus, FinisherTimeline.MeleeSquash(FinisherTimeline.PreviewProfile));
            approach.Start(hitSeconds);
        }
        try
        {
            if (pendingTargets.Length > 0 && execution.CanContinue())
            {
                await playPresentation(execution, pendingTargets);
            }
        }
        finally
        {
            approach?.ReleasePrediction();
            if (execution.Results.Count > 0)
            {
                command.AddResultsInternal(execution.Results);
            }

            try
            {
                CombatManager.Instance.History.CreatureAttacked(
                    combatState,
                    attacker,
                    execution.Results);
            }
            finally
            {
                await Hook.AfterAttack(combatState, choiceContext, command);
            }
        }

        return execution;
    }

    private sealed class Execution(
        AttackCommand command,
        PlayerChoiceContext choiceContext,
        Creature attacker,
        ICombatState? combatState,
        decimal damage)
    {
        internal AttackCommand Command => command;
        internal List<DamageResult> Results { get; } = [];

        internal bool CanContinue() =>
            combatState is not null
            && attacker.IsAlive
            && ReferenceEquals(attacker.CombatState, combatState)
            && combatState.ContainsCreature(attacker)
            && combatState.IsLiveCombat()
            && !CombatManager.Instance.IsOverOrEnding;

        internal bool CanHit(Creature target) =>
            CanContinue()
            && ReferenceEquals(target.CombatState, combatState)
            && combatState?.ContainsCreature(target) == true
            && target.IsAlive
            && target.Side != attacker.Side
            && target.IsHittable;

        internal bool WillConnect(Creature target) =>
            CanHit(target)
            && (target.GetPower<EvasionPower>() is not { } evasion
            || !evasion.CanEvade(target, command.DamageProps, attacker));

        internal bool WillPenetrate(Creature target)
        {
            if (!WillConnect(target)) return false;
            decimal preview = Hook.ModifyDamage(combatState!.RunState, combatState, target, attacker,
                damage, command.DamageProps, null,
#if !NINJASLAYER_LEGACY_DAMAGE_API
                null,
#endif
                ModifyDamageHookType.All, CardPreviewMode.None, out _);
            decimal block = command.DamageProps.HasFlag(ValueProp.Unblockable)
                ? 0 : (target.PetOwner?.Creature ?? target).Block;
            return decimal.Floor(preview) > block;
        }

        internal async Task<DarkStrikeImpactOutcome> Deal(Creature target)
        {
            if (!CanHit(target))
            {
                return new DarkStrikeImpactOutcome(false, false, 0, CanContinue());
            }

            IReadOnlyList<DamageResult> results = await Deal([target]);
            DamageResult[] targetResults = results
                .Where(result => ReferenceEquals(result.Receiver, target))
                .ToArray();
            bool connected = targetResults.Length > 0;

            return new DarkStrikeImpactOutcome(
                connected,
                connected && targetResults.All(result => result.WasFullyBlocked),
                results
                    .Select(result => DarkNinjaCombatMath.ResolveDarkStrikeHealing(
                        result.BlockedDamage,
                        result.UnblockedDamage,
                        result.OverkillDamage))
                    .DefaultIfEmpty(0)
                    .Max(),
                CanContinue(),
                targetResults.Any(result => result.UnblockedDamage > 0));
        }

        internal async Task<IReadOnlyList<DamageResult>> Deal(IEnumerable<Creature> targets)
        {
            if (!CanContinue())
            {
                return [];
            }

            FinisherApproach.ReachImpact(attacker);
            List<DamageResult> results = (await CreatureCmd.Damage(
                    choiceContext,
                    targets,
                    damage,
                    command.DamageProps,
                    attacker,
                    null
#if !NINJASLAYER_LEGACY_DAMAGE_API
                    , null
#endif
                ))
                .ToList();
            Results.AddRange(results);
            return results;
        }
    }
}
