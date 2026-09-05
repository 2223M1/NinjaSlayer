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

    internal static bool IsExcludedAttackCard(CardModel card) =>
        card.Tags.Contains(CardTag.Shiv)
        || card.Tags.Contains(NinjaSlayerCardTags.Shuriken);

    internal static bool TryCreateSession(
        FinisherAttackSpec spec,
        AttackCommand? command,
        string entryPoint,
        [NotNullWhen(true)] out FinisherSession? session)
    {
        session = null;
        if (IsExcludedAttackCard(spec.Card)
            || spec.Card.Owner?.Creature is not { } owner
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
                    forecast.ResolvedHits),
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

    internal static bool TryCreateYamotoKokiSession(
        Creature owner,
        NCreature ownerNode,
        NCreature focusNode,
        IReadOnlyList<Creature> enemies,
        Func<Creature, decimal> damage,
        [NotNullWhen(true)] out FinisherSession? session)
    {
        session = null;
        List<Creature> primaryEnemies = enemies.Where(enemy => enemy.IsPrimaryEnemy).ToList();
        if (owner.Monster is not YamotoKokiMonster
            || owner.CombatState is not { } combatState
            || NCombatRoom.Instance is not { } room
            || primaryEnemies.Count == 0
            || !FinisherProtectionService.CanProtectLethalDamage(out _))
        {
            return false;
        }

        NCreature? primaryFocusNode = focusNode.Entity.IsPrimaryEnemy
            ? focusNode
            : primaryEnemies
                .Select(enemy => room.GetCreatureNode(enemy))
                .FirstOrDefault(node => node != null);
        if (primaryFocusNode == null)
        {
            return false;
        }

        if (FinisherSessionRegistry.HasRegisteredSessionForCombat(combatState, room))
        {
            return false;
        }

        var descriptor = new FinisherActionForecastDescriptor(
            damage,
            ValueProp.Move,
            HitCount: 1,
            Targeting: FinisherTargeting.All);
        if (FinisherForecast.EvaluateAction(owner, enemies, descriptor, out FinisherForecastResult forecast)
            != FinisherForecastOutcome.Guaranteed
            || !CombatCinematicCameraLease.TryAcquire(
                room,
                "Yamoto Koki finisher",
                out CombatCinematicCameraLease? camera))
        {
            return false;
        }

        if (!FinisherSessionRegistry.TryRegisterSession(
                new FinisherSessionRequest(
                    FinisherScenarioKind.YamotoKokiIaiSlash,
                    FinisherCompletionCondition.AllCandidatesLethal,
                    owner,
                    ownerNode,
                    primaryFocusNode,
                    primaryEnemies,
                    camera,
                    CardPlay: null,
                    RequiresAfterCardPlayed: false,
                    ResolvedHits: forecast.ResolvedHits),
                combatState,
                room,
                out session))
        {
            camera.Dispose();
            return false;
        }

        Entry.Logger.Info(
            $"Yamoto Koki finisher session {session.SessionId} started: victims={primaryEnemies.Count}.");
        return true;
    }

}
