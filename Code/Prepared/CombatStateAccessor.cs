using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;

namespace NinjaSlayer.Code.Prepared;

internal interface ICombatStateAccessor
{
    CombatStateAccessResult<ICombatState> Resolve(CardModel card, ICombatState? suppliedState);
}

internal sealed class CardCombatStateAccessor : ICombatStateAccessor
{
    public CombatStateAccessResult<ICombatState> Resolve(CardModel card, ICombatState? suppliedState)
    {
        ICombatState? cardState = card.CombatState ?? card.Owner?.Creature.CombatState;
        return CombatStateAccessPolicy.Resolve(suppliedState, cardState);
    }
}
