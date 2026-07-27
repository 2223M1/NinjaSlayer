using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Content;
using NinjaSlayer.Monsters;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class FinisherEligibilityService
{
    private static int CompatibilityWarningLogged;

    internal static bool IsExcludedAttackCard(CardModel card) =>
        card is ShurikenCard or GiantShurikenCard
        || card.Tags.Contains(CardTag.Shiv)
        || card.Tags.Contains(NinjaSlayerCardTags.Shuriken);

    internal static bool TryCreateSession(
        FinisherAttackSpec spec,
        AttackCommand? command,
        string entryPoint,
        IFinisherActionAdapter? actionAdapter,
        out FinisherSession? session)
    {
        session = null;
        if (!NinjaSlayerPatchCapabilities.FinisherEnabled
            || FinisherSessionRegistry.HasRegisteredSession()
            || IsExcludedAttackCard(spec.Card)
            || spec.Card.Owner?.Creature is not { } owner
            || owner.Player?.Character is not INinjaSlayerCharacter
            || owner.CombatState is not { } combatState
            || NCombatRoom.Instance is not { } room)
        {
            return false;
        }

        if (!GameCompatibility.Finisher.CanProtectLethalDamage(out string compatibilityReason))
        {
            if (Interlocked.Exchange(ref CompatibilityWarningLogged, 1) == 0)
            {
                FinisherLog.Warn(
                    $"NinjaSlayer enhanced finisher disabled for this process: {compatibilityReason} "
                    + $"supportedGame={GameCompatibility.SupportedGameVersion}.");
            }

            return false;
        }

        List<Creature> enemies = combatState.HittableEnemies.Where(enemy => enemy.IsAlive).ToList();
        if (enemies.Count == 0
            || FinisherForecast.Evaluate(owner, enemies, spec, command, out FinisherForecastResult forecast)
                != FinisherForecastOutcome.Guaranteed)
        {
            return false;
        }

        NCreature? ownerNode = room.GetCreatureNode(owner);
        Creature? focus = enemies
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

        bool jumpActive = JumpAnimation.IsActive(owner);
        actionAdapter ??= command != null
            && GameCompatibility.Finisher.TryReadAttackCommand(
                command,
                out GameCompatibility.AttackCommandState commandState)
                ? FinisherActionAdapters.Resolve(commandState, jumpActive)
                : jumpActive
                    ? FinisherActionAdapters.Fast
                    : FinisherActionAdapters.Stationary;

        if (!FinisherSessionRegistry.TryRegisterSession(
                new FinisherSessionRequest(
                    FinisherScenarioKind.NinjaSlayerAttack,
                    FinisherCompletionCondition.AllCandidatesLethal,
                    owner,
                    ownerNode,
                    focusNode,
                    enemies,
                    camera!,
                    actionAdapter,
                    spec.CardPlay,
                    forecast.RequiresAfterCardPlayed,
                    forecast.ResolvedHits),
                combatState,
                room,
                out session))
        {
            camera!.Dispose();
            return false;
        }

        FinisherLog.Info(
            $"NinjaSlayer finisher session {session!.SessionId} started: card={spec.Card.Id.Entry}, entry={entryPoint}, targeting={spec.Forecast.Targeting}, hits={forecast.ResolvedHits}.");
        return true;
    }

    internal static bool TryCreateYamotoKokiSession(
        Creature owner,
        NCreature ownerNode,
        NCreature focusNode,
        IReadOnlyList<Creature> enemies,
        Func<Creature, decimal> damage,
        out FinisherSession? session)
    {
        session = null;
        if (!NinjaSlayerPatchCapabilities.FinisherEnabled
            || FinisherSessionRegistry.HasRegisteredSession()
            || owner.Monster is not YamotoKokiMonster
            || owner.CombatState is not { } combatState
            || NCombatRoom.Instance is not { } room
            || enemies.Count == 0
            || !GameCompatibility.Finisher.CanProtectLethalDamage(out _))
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
                    focusNode,
                    enemies,
                    camera!,
                    FinisherActionAdapters.YamotoKokiIai,
                    CardPlay: null,
                    RequiresAfterCardPlayed: false,
                    ResolvedHits: forecast.ResolvedHits),
                combatState,
                room,
                out session))
        {
            camera!.Dispose();
            return false;
        }

        FinisherLog.Info(
            $"Yamoto Koki finisher session {session!.SessionId} started: victims={enemies.Count}.");
        return true;
    }
}
