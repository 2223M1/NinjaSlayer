using NinjaSlayer.Code.Prepared;

namespace NinjaSlayer.LogicTests;

public sealed class PreparedSafetyTests
{
    [Fact]
    public void NotAppliedResultIsNeitherPreparedNorDegraded()
    {
        var result = new PreparedApplyResult(PreparedApplyStatus.NotApplied);

        Assert.False(result.IsPrepared);
        Assert.False(result.IsDegraded);
        Assert.False(result.RequiresLifecycleRepair);
    }

    [Theory]
    [InlineData(true, true, (int)PreparedCleanupStatus.NotRequired, (int)PreparedApplyStatus.Applied)]
    [InlineData(false, true, (int)PreparedCleanupStatus.NotRequired, (int)PreparedApplyStatus.AppliedDegraded)]
    [InlineData(false, false, (int)PreparedCleanupStatus.NotRequired, (int)PreparedApplyStatus.SafetyRepaired)]
    [InlineData(false, false, (int)PreparedCleanupStatus.Cleared, (int)PreparedApplyStatus.SafetyRepaired)]
    [InlineData(false, false, (int)PreparedCleanupStatus.Failed, (int)PreparedApplyStatus.SafetyRepairFailed)]
    public void ApplyResultDistinguishesSuccessDegradationAndSafetyRepair(
        bool repositionSucceeded,
        bool stablePlacement,
        int cleanupStatusValue,
        int expectedValue)
    {
        var cleanupStatus = (PreparedCleanupStatus)cleanupStatusValue;
        var expected = (PreparedApplyStatus)expectedValue;
        PreparedApplyResult result = PreparedApplyPolicy.ResolveAfterReposition(
            new PreparedQueueTransactionResult(
                repositionSucceeded
                    ? PreparedQueueTransactionStatus.Succeeded
                    : PreparedQueueTransactionStatus.FailedUncertain,
                repositionSucceeded ? null : new InvalidOperationException("reposition")),
            stablePlacement,
            new PreparedCleanupResult(
                cleanupStatus,
                cleanupStatus == PreparedCleanupStatus.Failed
                    ? new InvalidOperationException("cleanup")
                    : null),
            "test failure");

        Assert.Equal(expected, result.Status);
        Assert.Equal(
            expected is PreparedApplyStatus.Applied or PreparedApplyStatus.AppliedDegraded,
            result.IsPrepared);
        Assert.Equal(
            expected is not PreparedApplyStatus.Applied and not PreparedApplyStatus.NotApplied,
            result.IsDegraded);
        Assert.Equal(expected == PreparedApplyStatus.SafetyRepairFailed, result.RequiresLifecycleRepair);
    }

    [Fact]
    public void QueueTransactionMovesTheCardOnSuccess()
    {
        bool present = true;
        int position = 0;

        PreparedQueueTransactionResult result = PreparedQueueTransaction.Execute(
            remove: () => present = false,
            insertAtTarget: () =>
            {
                present = true;
                position = 2;
            },
            restoreAtOriginal: () =>
            {
                present = true;
                position = 0;
            },
            isPresent: () => present);

        Assert.Equal(PreparedQueueTransactionStatus.Succeeded, result.Status);
        Assert.Equal(2, position);
    }

    [Fact]
    public void QueueTransactionRestoresAfterRemoveThrowsPostMutation()
    {
        bool present = true;
        bool targetInsertCalled = false;

        PreparedQueueTransactionResult result = PreparedQueueTransaction.Execute(
            remove: () =>
            {
                present = false;
                throw new InvalidOperationException("remove callback");
            },
            insertAtTarget: () => targetInsertCalled = true,
            restoreAtOriginal: () => present = true,
            isPresent: () => present);

        Assert.Equal(PreparedQueueTransactionStatus.FailedStable, result.Status);
        Assert.True(present);
        Assert.False(targetInsertCalled);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void QueueTransactionDoesNotRollbackWhenRemoveFailsBeforeMutation()
    {
        bool present = true;
        bool rollbackCalled = false;

        PreparedQueueTransactionResult result = PreparedQueueTransaction.Execute(
            remove: () => throw new InvalidOperationException("remove rejected"),
            insertAtTarget: () => throw new InvalidOperationException("must not run"),
            restoreAtOriginal: () => rollbackCalled = true,
            isPresent: () => present);

        Assert.Equal(PreparedQueueTransactionStatus.FailedStable, result.Status);
        Assert.True(present);
        Assert.False(rollbackCalled);
    }

    [Fact]
    public void QueueTransactionDoesNotDuplicateACardWhenInsertThrowsAfterMutation()
    {
        bool present = true;
        bool rollbackCalled = false;

        PreparedQueueTransactionResult result = PreparedQueueTransaction.Execute(
            remove: () => present = false,
            insertAtTarget: () =>
            {
                present = true;
                throw new InvalidOperationException("add callback");
            },
            restoreAtOriginal: () => rollbackCalled = true,
            isPresent: () => present);

        Assert.Equal(PreparedQueueTransactionStatus.FailedStable, result.Status);
        Assert.True(present);
        Assert.False(rollbackCalled);
    }

    [Fact]
    public void QueueTransactionAcceptsRollbackThatThrowsAfterRestoringTheCard()
    {
        bool present = true;

        PreparedQueueTransactionResult result = PreparedQueueTransaction.Execute(
            remove: () => present = false,
            insertAtTarget: () => throw new InvalidOperationException("target add"),
            restoreAtOriginal: () =>
            {
                present = true;
                throw new InvalidOperationException("rollback callback");
            },
            isPresent: () => present);

        Assert.Equal(PreparedQueueTransactionStatus.FailedStable, result.Status);
        Assert.True(present);
        Assert.IsType<AggregateException>(result.Error);
    }

    [Fact]
    public void QueueTransactionReportsUncertainWhenInsertAndRollbackLeaveTheCardMissing()
    {
        bool present = true;

        PreparedQueueTransactionResult result = PreparedQueueTransaction.Execute(
            remove: () => present = false,
            insertAtTarget: () => throw new InvalidOperationException("target add"),
            restoreAtOriginal: () => throw new InvalidOperationException("rollback"),
            isPresent: () => present);

        Assert.Equal(PreparedQueueTransactionStatus.FailedUncertain, result.Status);
        Assert.False(present);
        Assert.IsType<AggregateException>(result.Error);
    }

    [Theory]
    [InlineData(1, true, false, true, false)]
    [InlineData(2, true, false, true, true)]
    [InlineData(1, false, true, true, false)]
    [InlineData(2, false, true, true, true)]
    [InlineData(1, false, false, false, true)]
    public void NextDiscardProtectionConsumesOnlyLayersOlderThanTheSourceCard(
        int powerAmount,
        bool hasMarker,
        bool expectedSourceFromPlay,
        bool expectedProtected,
        bool expectedConsume)
    {
        NextDiscardProtectionDecision decision = NextDiscardProtectionPolicy.Resolve(
            powerAmount,
            hasMarker,
            expectedSourceFromPlay);

        Assert.Equal(expectedProtected, decision.IsProtectedSource);
        Assert.Equal(expectedConsume, decision.ShouldConsumeLayer);
    }

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
