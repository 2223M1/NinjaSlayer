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
        Func<int, Task> perHit)
    {
        if (hits <= 0)
        {
            return;
        }

        XAttackComboMovement.BeginCombo(creature);
        await SpinComboAudio.PlaySequence(creature, hits, perHitDelay, async () =>
        {
            XAttackComboContext.Begin(hits);
            try
            {
                for (int i = 0; i < hits; i++)
                {
                    XAttackComboContext.CurrentHitIndex = i;
                    await perHit(i);
                }
            }
            finally
            {
                await XAttackComboMovement.EndCombo(creature);
                XAttackComboContext.End();
            }
        });
    }
}
