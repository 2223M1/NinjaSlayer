using MegaCrit.Sts2.Core.Entities.Creatures;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Nodes;

namespace NinjaSlayer.Code.ExternalAnimations;

public static class TornadoFistSpinAnimation
{
    public const string TriggerName = "TornadoFistSpin";
    public const string CueTriggerName = "TornadoFistCue";
    public const float TurnSeconds = 0.15f;

    internal static void NotifyHitTriggered(Creature target, string trigger)
    {
        if (trigger != "Hit" || NinjaSlayerAttackExecution.CurrentCommand is not { ModelSource: TornadoFistRedesignV1 } command
            || command.Attacker is not { } attacker || target.Side == attacker.Side
            || NinjaSlayerFinisherCinematic.IsMovementOwned(attacker)) return;
        NinjaSlayerAimPose? pose = NinjaSlayerAimPose.Get(attacker);
        if (pose is not { IsEmpoweredTornado: true, UseTornadoHitStop: true }) return;
        float normal = NinjaSlayerAttackExecution.IsFinalSequenceHit ? .05f : .035f;
        float seconds = CombatActionTimingRuntime.Resolve(normal, normal * .5f);
        if (seconds <= 0f) return;
        pose.PauseTornadoSpin(seconds);
        TornadoHurtPause.Start(target, seconds);
    }
}
