namespace NinjaSlayer.Code.Prepared;

internal enum PreparedDrawStartDecision
{
    Continue,
    CombatEnded,
    Prevented
}

internal enum PreparedDrawDecision
{
    Draw,
    Shuffle,
    StopCombatEnded,
    StopHandFull,
    StopNoCards
}

internal static class PreparedDrawPolicy
{
    public static PreparedDrawStartDecision DecideStart(bool combatActive, bool drawAllowed)
    {
        if (!combatActive)
        {
            return PreparedDrawStartDecision.CombatEnded;
        }

        return drawAllowed
            ? PreparedDrawStartDecision.Continue
            : PreparedDrawStartDecision.Prevented;
    }

    public static int RequestedDraws(decimal count) =>
        count > 0m ? (int)Math.Ceiling(count) : 0;

    public static PreparedDrawDecision DecideNext(
        bool combatActive,
        int handCount,
        int maxHandSize,
        int drawableDrawPileCards,
        int discardPileCards)
    {
        if (!combatActive)
        {
            return PreparedDrawDecision.StopCombatEnded;
        }
        if (handCount >= maxHandSize)
        {
            return PreparedDrawDecision.StopHandFull;
        }
        if (drawableDrawPileCards > 0)
        {
            return PreparedDrawDecision.Draw;
        }
        if (discardPileCards > 0)
        {
            return PreparedDrawDecision.Shuffle;
        }

        return PreparedDrawDecision.StopNoCards;
    }

    public static int FindFirstDrawableIndex(IEnumerable<bool> preparedFlags)
    {
        int index = 0;
        foreach (bool isPrepared in preparedFlags)
        {
            if (!isPrepared)
            {
                return index;
            }
            index++;
        }

        return -1;
    }
}
