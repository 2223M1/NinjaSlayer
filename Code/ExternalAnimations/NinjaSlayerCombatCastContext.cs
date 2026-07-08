using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;

namespace NinjaSlayer.Code.ExternalAnimations;

public static class NinjaSlayerCombatCastContext
{
    public static CardModel? GetCurrentCard(Creature creature)
    {
        var history = CombatManager.Instance?.History;
        if (history == null)
        {
            return null;
        }

        var finishedPlays = history.CardPlaysFinished
            .Where(e => e.Actor == creature)
            .Select(e => e.CardPlay)
            .ToHashSet();

        return history.CardPlaysStarted
            .Where(e => e.Actor == creature)
            .LastOrDefault(e => !finishedPlays.Contains(e.CardPlay))
            ?.CardPlay.Card;
    }
}
