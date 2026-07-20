using MegaCrit.Sts2.Core.Entities.Creatures;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.ExternalAnimations;

/// <summary>
/// Orchestrates X-cost attack spin SFX, combo context, and lunge movement for NinjaSlayer X attack cards.
/// Cards must inherit <see cref="Cards.NinjaSlayerXAttackCard"/>; vanilla WithHitCount(X) is not covered.
/// </summary>
public static class NinjaSlayerXAttackSequence
{
    public static async Task Run(
        Creature creature,
        int hits,
        float perHitDelay,
        float audioHitDuration,
        Func<int, Task<bool>> perHit)
    {
        if (hits <= 0)
        {
            return;
        }

        XAttackComboMovement.BeginCombo(creature);
        bool useSlowAttack = hits <= 4 || NinjaSlayerFormState.IsFullyReleasedNaraku(creature);
        Func<Action, Task> executeHits = async finishSpinEarly =>
        {
            XAttackComboContext.Begin(hits);
            try
            {
                for (int i = 0; i < hits; i++)
                {
                    XAttackComboContext.CurrentHitIndex = i;
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
                await XAttackComboMovement.EndCombo(creature);
                XAttackComboContext.End();
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
