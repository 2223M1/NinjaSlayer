using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Content;
using NinjaSlayer.Monsters;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Code.ExternalAnimations;

internal readonly record struct DarkStrikeImpactOutcome(
    bool Connected,
    bool FullyBlocked,
    int Healing,
    bool ShouldContinue);

internal static class DarkNinjaAttackExecution
{
    private const string DeathSlashImpactSfx =
        "event:/sfx/enemy/enemy_attacks/vantom/vantom_dismember";

    internal static async Task<AttackCommand> PlayDeathSlash(
        DarkNinjaMonster monster,
        IReadOnlyList<Creature> targets,
        int damage)
    {
        Execution execution = await Execute(
            monster,
            targets,
            damage,
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

    internal static async Task<IReadOnlyList<Creature>> PlayDarkStrike(
        DarkNinjaMonster monster,
        IReadOnlyList<Creature> targets,
        int damage)
    {
        Execution execution = await Execute(
            monster,
            targets,
            damage,
            async (execution, targets) =>
            {
                await DarkNinjaSpecialAttackPresentation.PlayDarkStrike(
                    monster.Creature,
                    targets,
                    execution.CanHit,
                    execution.Deal);
            });
        return execution.ConnectedTargets;
    }

    private static async Task<Execution> Execute(
        DarkNinjaMonster monster,
        IReadOnlyList<Creature> targets,
        int damage,
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
        try
        {
            if (pendingTargets.Length > 0 && execution.CanContinue())
            {
                await playPresentation(execution, pendingTargets);
            }
        }
        finally
        {
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
        internal List<Creature> ConnectedTargets { get; } = [];

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
            if (connected)
            {
                ConnectedTargets.Add(target);
            }

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
                CanContinue());
        }

        internal async Task<IReadOnlyList<DamageResult>> Deal(IEnumerable<Creature> targets)
        {
            if (!CanContinue())
            {
                return [];
            }

            List<DamageResult> results = (await GameCompatibility.Damage.Deal(
                    choiceContext,
                    targets,
                    damage,
                    command.DamageProps,
                    attacker,
                    null,
                    null))
                .ToList();
            Results.AddRange(results);
            return results;
        }
    }
}
