using NinjaSlayer.Code.Transition;

namespace NinjaSlayer.LogicTests;

public sealed class TransitionLoadSmoothingTests
{
    [Fact]
    public void DeferredRequestsCoalesceAndFollowTheCurrentSession()
    {
        var state = new TransitionGcDeferralState();
        Assert.False(state.Begin(17));
        Assert.True(state.TryDefer());
        Assert.True(state.TryDefer());
        Assert.True(state.Begin(18));

        int collections = 0;
        Assert.Null(state.Complete(17, () => collections++));
        Assert.True(state.IsActive);
        Assert.Null(state.Complete(18, () => collections++));
        Assert.Equal(1, collections);
        Assert.False(state.IsActive);
    }

    [Fact]
    public void DeferredCollectionFailureIsReturned()
    {
        var state = new TransitionGcDeferralState();
        state.Begin(20);
        state.TryDefer();

        Exception? failure = state.Complete(
            20,
            () => throw new InvalidOperationException("background collection unavailable"));

        Assert.IsType<InvalidOperationException>(failure);
    }

    [Fact]
    public void SupersedingSessionInheritsAndEndsNoGcRegionOnce()
    {
        var state = new TransitionNoGcRegionState();
        int starts = 0;
        int ends = 0;
        var counts = new TransitionGcCounts(3, 2, 1);

        state.Begin(31, () =>
        {
            starts++;
            return true;
        }, () => counts);
        state.Begin(32, () =>
        {
            starts++;
            return true;
        }, () => counts);
        state.Complete(31, counts, () => true, () => ends++);
        state.Complete(32, counts, () => true, () => ends++);
        state.Complete(32, counts, () => true, () => ends++);

        Assert.Equal(1, starts);
        Assert.Equal(1, ends);
    }

    [Fact]
    public void ObservedCollectionDoesNotEndAnotherNoGcRegion()
    {
        var state = new TransitionNoGcRegionState();
        int activeChecks = 0;
        int ends = 0;
        state.Begin(40, () => true, () => new TransitionGcCounts(1, 1, 0));

        state.Complete(
            40,
            new TransitionGcCounts(2, 1, 0),
            () =>
            {
                activeChecks++;
                return true;
            },
            () => ends++);

        Assert.Equal(0, activeChecks);
        Assert.Equal(0, ends);
    }
}
