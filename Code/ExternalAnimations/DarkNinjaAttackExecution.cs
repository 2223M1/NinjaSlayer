using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
#if !NINJASLAYER_CHANNEL_STABLE
using MegaCrit.Sts2.Core.Models.Powers;
#endif
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
    bool HpLost = false)
{
    internal Creature? Receiver { get; init; }
}

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
                            VfxCmd.PlayOnSide(CombatSide.Player,
                                VfxCmd.giantHorizontalSlashPath, monster.Creature.CombatState!);
                            NinjaSlayerCombatAudioSet.Play(DeathSlashImpactSfx);

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
                        var player = target.Player;
                        if (player == null) return await execution.Deal(target);
                        CardModel[] candidates = CardPile.GetCards(player, PileType.Draw, PileType.Discard)
                                .Where(card => card.DeckVersion != null).ToArray();
                        var previous = monster.DarkStrikeDamageConfirmed;
                        bool stolen = false;
                        monster.DarkStrikeDamageConfirmed = async (receiver, result) =>
                        {
                            if (stolen || receiver != target || result.Receiver != target
                                || result.UnblockedDamage <= 0)
                                return;
                            stolen = true;
                            await monster.StealFrom(candidates);
                        };
                        try
                        {
                            DarkStrikeImpactOutcome outcome = await execution.Deal(target);
                            // Lethal retaliation detaches the attacker before its damage hook can run.
                            if (!stolen && outcome.HpLost)
                            {
                                stolen = true;
                                await monster.StealFrom(candidates);
                            }
                            return outcome;
                        }
                        finally { monster.DarkStrikeDamageConfirmed = previous; }
                    }, returnSide: returnSide, predictContact: execution.PredictContact);
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
            approach = FinisherApproach.Create(actorNode, focus);
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

        internal (Creature Receiver, bool Penetrates) PredictContact(Creature target)
        {
            if (!WillConnect(target)) return (target, false);
            decimal preview = Hook.ModifyDamage(combatState!.RunState, combatState, target, attacker,
                damage, command.DamageProps, null,
                null,
                ModifyDamageHookType.All, CardPreviewMode.None, out _);
            decimal incoming = decimal.Floor(preview);
            decimal block = command.DamageProps.HasFlag(ValueProp.Unblockable)
                ? 0 : (target.PetOwner?.Creature ?? target).Block;
#if !NINJASLAYER_CHANNEL_STABLE
            // Wuppy receives powered attack damage before the owner's block. Only
            // overflow can reach the player. The native command remains authoritative.
            if (command.DamageProps.IsPoweredAttack() && incoming > 0
                && target.Pets.FirstOrDefault(p => p.IsAlive && p.HasPower<WuppysLoyaltyPower>()) is { } wuppy
                && incoming <= wuppy.CurrentHp + block)
                return (wuppy, true);
#endif
            return (target, incoming > block);
        }

        internal async Task<DarkStrikeImpactOutcome> Deal(Creature target)
        {
            if (!CanHit(target))
            {
                return new DarkStrikeImpactOutcome(false, false, 0, CanContinue());
            }

            IReadOnlyList<DamageResult> results = await Deal([target]);
            DamageResult? directHit = results.FirstOrDefault(result => result.Receiver == target && result.UnblockedDamage > 0);
            DamageResult? impact = directHit ?? results.FirstOrDefault(result => result.UnblockedDamage > 0)
                ?? results.FirstOrDefault(result => result.Receiver == target);

            return new DarkStrikeImpactOutcome(
                impact != null,
                impact?.WasFullyBlocked == true,
                results
                    .Select(result => DarkNinjaCombatMath.ResolveDarkStrikeHealing(
                        result.BlockedDamage,
                        result.UnblockedDamage,
                        result.OverkillDamage))
                    .DefaultIfEmpty(0)
                    .Max(),
                CanContinue(),
                directHit != null) { Receiver = impact?.Receiver };
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
                    , null
                ))
                .ToList();
            Results.AddRange(results);
            return results;
        }
    }
}
