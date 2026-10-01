using MegaCrit.Sts2.Core.Entities.Creatures;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.ExternalAnimations;

public static class NinjaSlayerXAttackSequence
{
    public static async Task Run(Creature creature, int hits, Func<int, Task<bool>> perHit, bool heldApproach = false)
    {
        if (hits <= 0)
        {
            if (heldApproach) Nodes.NinjaSlayerAimPose.Get(creature)?.ReleaseEmptyTornado();
            return;
        }
        using var cadence = NinjaSlayerAttackExecution.EnterSequence(hits);
        using IDisposable? suppression = heldApproach ? XAttackAudioContext.Suppress() : null;
        SpinComboAudio? spin = null;
        try
        {
            if (heldApproach)
            {
                XAttackComboMovement.BeginCombo(creature, TornadoFistSpinAnimation.TurnSeconds);
                float duration = SpinComboAudio.RemainingSeconds(creature, hits);
                TornadoAudioMode mode = TornadoSpinTiming.Select(hits, duration,
                    NinjaSlayerFormState.GetPresentation(creature).ForcePerHitComboAudio);
                if (mode != TornadoAudioMode.PerHit)
                    spin = SpinComboAudio.Start(creature);
            }
            for (int i = 0; i < hits; i++)
            {
                cadence.SetHit(i);
                if (heldApproach && spin == null)
                    NinjaSlayerCombatAudioSet.Play(NinjaSlayerCombatAudioSet.For(creature).FastAttack);
                bool targetKilled = await perHit(i);
                if (targetKilled && !NinjaSlayerFinisherCinematic.IsMovementOwned(creature))
                {
                    break;
                }
            }
        }
        finally
        {
            if (spin != null && Godot.GodotObject.IsInstanceValid(spin)) spin.Finish();
            if (heldApproach) await XAttackComboMovement.EndCombo(creature);
        }
    }
}
