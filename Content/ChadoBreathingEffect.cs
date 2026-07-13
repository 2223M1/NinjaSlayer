using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Content;

public sealed class ChadoBreathingEffect
{
    private int _healedThisCombat;

    public int HealedThisCombat => _healedThisCombat;

    public void Reset() => _healedThisCombat = 0;

    public async Task<bool> TryApply(
        Player owner,
        int healAmount,
        int maxHealPerCombat,
        Action? beforeHeal = null)
    {
        if (owner.Creature.IsDead || NinjaSlayerActions.ChadoInHandCount(owner) <= 0)
        {
            return false;
        }

        int remaining = maxHealPerCombat - _healedThisCombat;
        if (remaining <= 0)
        {
            return false;
        }

        int heal = Math.Min(healAmount, remaining);
        beforeHeal?.Invoke();
        await CreatureCmd.Heal(owner.Creature, heal);
        _healedThisCombat += heal;

        if (owner.Creature.HasPower<NarakuPower>())
        {
            await NinjaSlayerActions.ExitNaraku(owner.Creature);
        }

        return true;
    }
}
