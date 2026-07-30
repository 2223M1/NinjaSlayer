using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;

namespace NinjaSlayer.Code.Prepared;

internal static class CardCombatStateAccessor
{
    public static CombatStateAccessResult<ICombatState> Resolve(
        CardModel card,
        ICombatState? suppliedState)
    {
        ICombatState? cardState = card.CombatState ?? card.Owner?.Creature.CombatState;
        return CombatStateAccessPolicy.Resolve(suppliedState, cardState);
    }
}
