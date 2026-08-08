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

internal static class DarkNinjaAttackExecution
{
    private const string DeathSlashImpactSfx =
        "event:/sfx/enemy/enemy_attacks/vantom/vantom_dismember";
    internal static Task<AttackCommand> PlayDeathSlash(
        DarkNinjaMonster monster,
        int damage) =>
        Execute(
            monster,
            damage,
            async (execution, targets) =>
            {
                await DarkNinjaSpecialAttackPresentation.PlayDeathSlash(
                    monster.Creature,
                    async () =>
                    {
                        Creature[] liveTargets = targets.Where(target => target.IsAlive).ToArray();
                        if (liveTargets.Length > 0)
                        {
                            Creature[] connectedTargets = liveTargets
                                .Where(execution.WillConnect)
                                .ToArray();
                            if (connectedTargets.Length > 0)
                            {
                                VfxCmd.PlayOnCreatureCenters(
                                    connectedTargets,
                                    VfxCmd.giantHorizontalSlashPath);
                                NinjaSlayerCombatAudioSet.Play(DeathSlashImpactSfx);
                            }

                            await execution.Deal(liveTargets);
                        }
                    });
            });

    internal static Task<AttackCommand> PlayDarkStrike(
        DarkNinjaMonster monster,
        int damage) =>
        Execute(
            monster,
            damage,
            async (execution, targets) =>
            {
                await DarkNinjaSpecialAttackPresentation.PlayDarkStrike(
                    monster.Creature,
                    targets,
                    async target =>
                    {
                        if (execution.WillConnect(target))
                        {
                            NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.DarkNinjaStabEvent);
                            VfxCmd.PlayOnCreatureCenter(target, VfxCmd.dramaticStabPath);
                        }

                        IReadOnlyList<DamageResult> results = await execution.Deal([target]);
                        return results
                            .Select(result => DarkNinjaCombatMath.ResolveDarkStrikeHealing(
                                result.BlockedDamage,
                                result.UnblockedDamage,
                                result.OverkillDamage))
                            .DefaultIfEmpty(0)
                            .Max();
                    });
            });

    private static async Task<AttackCommand> Execute(
        DarkNinjaMonster monster,
        int damage,
        Func<Execution, IReadOnlyList<Creature>, Task> playPresentation)
    {
        Creature attacker = monster.Creature;
        AttackCommand command = DamageCmd.Attack(damage).FromMonster(monster);
        ICombatState? combatState = attacker.CombatState;
        if (combatState == null
            || attacker.IsDead
            || (CombatManager.Instance.IsOverOrEnding && combatState.IsLiveCombat()))
        {
            return command;
        }

        var choiceContext = new BlockingPlayerChoiceContext();
        var execution = new Execution(
            command,
            choiceContext,
            attacker,
            damage);
        Creature[] targets = combatState.PlayerCreatures
            .Where(target => target.IsAlive)
            .ToArray();

        await Hook.BeforeAttack(combatState, command);
        try
        {
            if (targets.Length > 0 && attacker.IsAlive)
            {
                await playPresentation(execution, targets);
            }
        }
        finally
        {
            if (execution.Results.Count > 0)
            {
                command.AddResultsInternal(execution.Results);
            }

            CombatManager.Instance.History.CreatureAttacked(
                combatState,
                attacker,
                execution.Results);
            await Hook.AfterAttack(combatState, choiceContext, command);
        }

        return command;
    }

    private sealed class Execution(
        AttackCommand command,
        PlayerChoiceContext choiceContext,
        Creature attacker,
        decimal damage)
    {
        internal List<DamageResult> Results { get; } = [];

        internal bool WillConnect(Creature target) =>
            target.GetPower<EvasionPower>() is not { } evasion
            || !evasion.CanEvade(target, command.DamageProps, attacker);

        internal async Task<IReadOnlyList<DamageResult>> Deal(IEnumerable<Creature> targets)
        {
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
