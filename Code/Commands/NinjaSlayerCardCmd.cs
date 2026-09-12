using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace NinjaSlayer.Code.Commands;

public static class NinjaSlayerCardCmd
{
    public static async Task AddGeneratedCard<T>(Player owner, PileType pile, CardPilePosition position = CardPilePosition.Bottom)
        where T : CardModel
    {
        ICombatState combatState = owner.Creature.CombatState ?? throw new InvalidOperationException("Generated cards require an active combat state.");
        CardPileAddResult result = await CardPileCmd.AddGeneratedCardToCombat(combatState.CreateCard<T>(owner), pile, owner, position);
        if (pile is PileType.Draw or PileType.Discard)
        {
            CardCmd.PreviewCardPileAdd(result);
        }
    }
}
