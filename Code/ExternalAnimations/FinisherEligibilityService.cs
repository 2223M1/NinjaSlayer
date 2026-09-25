using System.Diagnostics.CodeAnalysis;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Content;
using NinjaSlayer.Monsters;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class FinisherEligibilityService
{
    private static bool CompatibilityWarningLogged;

    internal static bool TryCreateSession(
        FinisherAttackSpec spec,
        AttackCommand? command,
        string entryPoint,
        [NotNullWhen(true)] out FinisherSession? session)
    {
        session = null;
        if (spec.Card.Owner?.Creature is not { } owner
            || owner.Player?.Character is not INinjaSlayerCharacter
            || owner.CombatState is not { } combatState
            || NCombatRoom.Instance is not { } room)
        {
            return false;
        }

        if (FinisherSessionRegistry.HasRegisteredSessionForCombat(combatState, room))
        {
            return false;
        }

        if (!FinisherProtectionService.CanProtectLethalDamage(out string compatibilityReason))
        {
            if (!CompatibilityWarningLogged)
            {
                CompatibilityWarningLogged = true;
                Entry.Logger.Warn(
                    $"NinjaSlayer enhanced finisher disabled for this process: {compatibilityReason}");
            }

            return false;
        }

        List<Creature> enemies = combatState.HittableEnemies.Where(enemy => enemy.IsAlive).ToList();
        List<Creature> primaryEnemies = enemies.Where(enemy => enemy.IsPrimaryEnemy).ToList();
        if (primaryEnemies.Count == 0)
        {
            return false;
        }

        FinisherForecastOutcome forecastOutcome = FinisherForecast.Evaluate(
            owner,
            enemies,
            spec,
            command,
            out FinisherForecastResult forecast);
        if (forecastOutcome != FinisherForecastOutcome.Guaranteed)
        {
            return false;
        }

        NCreature? ownerNode = room.GetCreatureNode(owner);
        Creature? focus = primaryEnemies
            .Select(enemy => (Enemy: enemy, Node: room.GetCreatureNode(enemy)))
            .Where(pair => pair.Node != null)
            .OrderBy(pair => pair.Node!.GlobalPosition.X)
            .Select(pair => pair.Enemy)
            .FirstOrDefault();
        NCreature? focusNode = room.GetCreatureNode(focus);
        if (ownerNode == null || focus == null || focusNode == null
            || !CombatCinematicCameraLease.TryAcquire(room, "NinjaSlayer finisher", out CombatCinematicCameraLease? camera))
        {
            return false;
        }

        if (!FinisherSessionRegistry.TryRegisterSession(
                new FinisherSessionRequest(
                    FinisherScenarioKind.NinjaSlayerAttack,
                    FinisherCompletionCondition.AllCandidatesLethal,
                    owner,
                    ownerNode,
                    focusNode,
                    primaryEnemies,
                    camera,
                    spec.CardPlay,
                    forecast.RequiresAfterCardPlayed,
                    forecast.ResolvedHits,
                    RangedAction: FinisherRangedAction.For(owner) is { } ranged && ranged.Source == spec.Card
                        ? ranged : null),
                combatState,
                room,
                out session))
        {
            camera.Dispose();
            return false;
        }

        Entry.Logger.Info(
            $"NinjaSlayer finisher session {session.SessionId} started: card={spec.Card.Id.Entry}, entry={entryPoint}, targeting={spec.Forecast.Targeting}, hits={forecast.ResolvedHits}.");
        return true;
    }

    internal static FinisherSession? CreateActionSession(
        Creature owner,
        FinisherActionForecastDescriptor descriptor)
    {
        if (!(FriendlyCompanionTargeting.IsFriendlyCompanion(owner)
                || owner.Player?.Character is INinjaSlayerCharacter
                || owner is { Side: CombatSide.Player, PetOwner: not null, Monster: YamotoKokiOrigamiMissile })
            || owner.CombatState is not { } combatState
            || NCombatRoom.Instance is not { } room
            || owner.GetCreatureNode() is not { } ownerNode
            || !FinisherProtectionService.CanProtectLethalDamage(out _))
        {
            return null;
        }

        var enemies = combatState.HittableEnemies.Where(enemy => enemy.IsAlive).ToArray();
        var primaryEnemies = enemies.Where(enemy => enemy.IsPrimaryEnemy).ToArray();
        NCreature? primaryFocusNode = descriptor.SingleTarget is { IsPrimaryEnemy: true } target
            ? target.GetCreatureNode()
            : primaryEnemies.Select(enemy => enemy.GetCreatureNode()).Where(node => node != null)
                .OrderBy(node => Math.Abs(node!.Visuals.Bounds.GetGlobalRect().GetCenter().X
                    - ownerNode.Visuals.Bounds.GetGlobalRect().GetCenter().X)).FirstOrDefault();
        if (primaryFocusNode == null)
        {
            return null;
        }

        if (FinisherSessionRegistry.HasRegisteredSessionForCombat(combatState, room))
        {
            return null;
        }

        if (FinisherForecast.EvaluateAction(owner, enemies, descriptor, out FinisherForecastResult forecast)
            != FinisherForecastOutcome.Guaranteed
            || !CombatCinematicCameraLease.TryAcquire(
                room,
                "Action finisher",
                out CombatCinematicCameraLease? camera))
        {
            return null;
        }

        if (!FinisherSessionRegistry.TryRegisterSession(
                new FinisherSessionRequest(
                    owner.Player != null ? FinisherScenarioKind.NinjaSlayerAttack : FinisherScenarioKind.CompanionAttack,
                    FinisherCompletionCondition.AllCandidatesLethal,
                    owner,
                    ownerNode,
                    primaryFocusNode,
                    primaryEnemies,
                    camera,
                    CardPlay: null,
                    RequiresAfterCardPlayed: false,
                    ResolvedHits: forecast.ResolvedHits,
                    RangedAction: FinisherRangedAction.For(owner)),
                combatState,
                room,
                out FinisherSession? session))
        {
            camera.Dispose();
            return null;
        }

        Entry.Logger.Info(
            $"Action by {owner} finisher session {session.SessionId} started: victims={primaryEnemies.Length}.");
        return session;
    }

}
