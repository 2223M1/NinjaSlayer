using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Cards;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Content;

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

        if (!FinisherSessionRegistry.TryRegisterSession(
                owner,
                ownerNode,
                focusNode,
                enemies,
                camera!,
                spec.CardPlay,
                forecast.RequiresAfterCardPlayed,
                forecast.ResolvedHits,
                combatState,
                room,
                out session))
        {
            camera!.Dispose();
            return false;
        }

        FinisherLog.Info(
            $"NinjaSlayer finisher session {session!.SessionId} started: card={spec.Card.Id.Entry}, entry={entryPoint}, targeting={spec.Targeting}, hits={forecast.ResolvedHits}.");
        return true;
    }
}
