using NinjaSlayer.Code.Prepared;

namespace NinjaSlayer.LogicTests;

public sealed class PreparedSafetyTests
{
    [Fact]
    public void DrawStartFailsClosedForCombatEndAndDrawPrevention()
    {
        Assert.Equal(
            PreparedDrawStartDecision.CombatEnded,
            PreparedDrawPolicy.DecideStart(combatActive: false, drawAllowed: true));
        Assert.Equal(
            PreparedDrawStartDecision.Prevented,
            PreparedDrawPolicy.DecideStart(combatActive: true, drawAllowed: false));
        Assert.Equal(
            PreparedDrawStartDecision.Continue,
            PreparedDrawPolicy.DecideStart(combatActive: true, drawAllowed: true));
    }

    [Fact]
    public void OnlyPreparedCardsStopUnlessDiscardCanBeShuffled()
    {
        Assert.Equal(
            PreparedDrawDecision.StopNoCards,
            PreparedDrawPolicy.DecideNext(true, 3, 10, drawableDrawPileCards: 0, discardPileCards: 0));
        Assert.Equal(
            PreparedDrawDecision.Shuffle,
            PreparedDrawPolicy.DecideNext(true, 3, 10, drawableDrawPileCards: 0, discardPileCards: 4));
    }

    [Fact]
    public void FullHandAndInterruptedDrawStopBeforePileMutation()
    {
        Assert.Equal(
            PreparedDrawDecision.StopHandFull,
            PreparedDrawPolicy.DecideNext(true, 10, 10, drawableDrawPileCards: 3, discardPileCards: 2));
        Assert.Equal(
            PreparedDrawDecision.StopCombatEnded,
            PreparedDrawPolicy.DecideNext(false, 0, 10, drawableDrawPileCards: 3, discardPileCards: 2));
    }

    [Fact]
    public void RequestedCountRoundsLikeTheOriginalDrawCommand()
    {
        Assert.Equal(0, PreparedDrawPolicy.RequestedDraws(-1m));
        Assert.Equal(0, PreparedDrawPolicy.RequestedDraws(0m));
        Assert.Equal(1, PreparedDrawPolicy.RequestedDraws(0.1m));
        Assert.Equal(3, PreparedDrawPolicy.RequestedDraws(2.01m));
    }

    [Fact]
    public void RequestLargerThanNormalCardsStopsAfterTheAvailableCards()
    {
        int requested = PreparedDrawPolicy.RequestedDraws(5m);
        int drawableCards = 2;
        int drawn = 0;
        for (int index = 0; index < requested; index++)
        {
            PreparedDrawDecision decision = PreparedDrawPolicy.DecideNext(
                combatActive: true,
                handCount: drawn,
                maxHandSize: 10,
                drawableDrawPileCards: drawableCards,
                discardPileCards: 0);
            if (decision != PreparedDrawDecision.Draw)
            {
                break;
            }
            drawableCards--;
            drawn++;
        }

        Assert.Equal(2, drawn);
        Assert.Equal(
            PreparedDrawDecision.StopNoCards,
            PreparedDrawPolicy.DecideNext(true, drawn, 10, drawableCards, 0));
    }

    [Fact]
    public void CurrentPileOrderAlwaysSelectsTheFirstNonPreparedCard()
    {
        Assert.Equal(2, PreparedDrawPolicy.FindFirstDrawableIndex([true, true, false, false]));
        Assert.Equal(0, PreparedDrawPolicy.FindFirstDrawableIndex([false, true, false]));
        Assert.Equal(-1, PreparedDrawPolicy.FindFirstDrawableIndex([true, true]));

        // A shuffle hook may reorder the list; selection is recomputed from its final order.
        Assert.Equal(1, PreparedDrawPolicy.FindFirstDrawableIndex([true, false, true, false]));
    }

    [Fact]
    public void StateIsReevaluatedAfterHooksInsteadOfUsingACachedDrawSnapshot()
    {
        Assert.Equal(
            PreparedDrawDecision.Draw,
            PreparedDrawPolicy.DecideNext(true, 2, 10, drawableDrawPileCards: 1, discardPileCards: 0));

        // Simulate AfterCardDrawn moving the card back into Draw: the next iteration sees it again.
        Assert.Equal(
            PreparedDrawDecision.Draw,
            PreparedDrawPolicy.DecideNext(true, 2, 10, drawableDrawPileCards: 1, discardPileCards: 0));
    }

    [Fact]
    public void PileChangeCleanupRequiresAConfirmedDrawPileExitInTheCurrentCombat()
    {
        Assert.True(PreparedSafetyPolicy.ShouldClearAfterPileChange(
            isPrepared: true,
            oldPileWasDraw: true,
            remainsInDrawPile: false,
            belongsToCurrentCombat: true));

        Assert.False(PreparedSafetyPolicy.ShouldClearAfterPileChange(true, true, true, true));
        Assert.False(PreparedSafetyPolicy.ShouldClearAfterPileChange(true, true, false, false));
        Assert.False(PreparedSafetyPolicy.ShouldClearAfterPileChange(true, false, false, true));
        Assert.False(PreparedSafetyPolicy.ShouldClearAfterPileChange(false, true, false, true));
    }

    [Fact]
    public void LifecycleCleanupPreservesOnlyValidPreparedCardsInTheDrawPile()
    {
        Assert.False(PreparedSafetyPolicy.ShouldClearAtLifecycleBoundary(true, true, true, false));
        Assert.True(PreparedSafetyPolicy.ShouldClearAtLifecycleBoundary(true, false, true, false));
        Assert.True(PreparedSafetyPolicy.ShouldClearAtLifecycleBoundary(true, true, false, false));
        Assert.True(PreparedSafetyPolicy.ShouldClearAtLifecycleBoundary(true, true, true, true));
        Assert.False(PreparedSafetyPolicy.ShouldClearAtLifecycleBoundary(false, false, false, true));
    }
}
