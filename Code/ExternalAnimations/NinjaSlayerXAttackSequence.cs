using MegaCrit.Sts2.Core.Entities.Creatures;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.ExternalAnimations;

/// <summary>
/// Orchestrates X-cost attack spin SFX and lunge movement for NinjaSlayer X attack cards.
/// Cards must inherit <see cref="Cards.NinjaSlayerXAttackCard"/>; vanilla WithHitCount(X) is not covered.
/// </summary>
public static class NinjaSlayerXAttackSequence
{
    public static async Task Run(
        Creature creature,
        int hits,
        float perHitDelay,
        float audioHitDuration,
        Func<int, Task<bool>> perHit,
        bool heldApproach = false)
    {
        if (hits <= 0)
        {
            if (heldApproach)
                Nodes.NinjaSlayerAimPose.Get(creature)?.ReleaseEmptyTornado();
            return;
        }

        using var cadence = NinjaSlayerAttackExecution.EnterSequence(hits);
        if (heldApproach)
            XAttackComboMovement.BeginCombo(creature, perHitDelay);
        bool useSlowAttack = hits <= 4
            || NinjaSlayerFormState.GetPresentation(creature).ForcePerHitComboAudio;
        Func<Action, Task> executeHits = async finishSpinEarly =>
        {
            try
            {
                for (int i = 0; i < hits; i++)
                {
                    cadence.SetHit(i);
                    if (useSlowAttack)
                    {
                        NinjaSlayerCombatAudioSet.Play(NinjaSlayerCombatAudioSet.For(creature).SlowAttack);
                    }

                    bool targetKilled = await perHit(i);
                    if (targetKilled && !NinjaSlayerFinisherCinematic.IsMovementOwned(creature))
                    {
                        finishSpinEarly();
                        break;
                    }
                }
            }
            finally
            {
                if (heldApproach)
                    await XAttackComboMovement.EndCombo(creature);
            }
        };

        if (useSlowAttack)
        {
            await SpinComboAudio.RunWithSuppressedAutomaticSfx(() => executeHits(static () => { }));
            return;
        }

        await SpinComboAudio.PlayTornadoFistSequence(creature, hits, audioHitDuration, executeHits);
    }
}
