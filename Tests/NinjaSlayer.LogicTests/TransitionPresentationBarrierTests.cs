using NinjaSlayer.Code.Transition;

namespace NinjaSlayer.LogicTests;

public sealed class TransitionPresentationBarrierTests
{
    [Fact]
    public async Task ReleaseStartsDeferredOperationsInRegistrationOrder()
    {
        var barrier = new TransitionPresentationBarrier();
        var order = new List<int>();

        Assert.True(barrier.TryDefer(
            () =>
            {
                order.Add(1);
                return Task.CompletedTask;
            },
            out Task first));
        Assert.True(barrier.TryDefer(
            () =>
            {
                order.Add(2);
                return Task.CompletedTask;
            },
            out Task second));
        Assert.Empty(order);

        Assert.True(barrier.Release());
        await Task.WhenAll(first, second);

        Assert.Equal([1, 2], order);
        Assert.Equal(TransitionPresentationDisposition.Released, barrier.Disposition);
        Assert.False(barrier.Release());
    }

    [Fact]
    public async Task DiscardCompletesWaitersWithoutRunningPresentation()
    {
        var barrier = new TransitionPresentationBarrier();
        var invoked = false;

        Assert.True(barrier.TryDefer(
            () =>
            {
                invoked = true;
                return Task.CompletedTask;
            },
            out Task completion));

        Assert.True(barrier.Discard());
        await completion;

        Assert.False(invoked);
        Assert.Equal(TransitionPresentationDisposition.Discarded, barrier.Disposition);
        Assert.False(barrier.Release());
    }

    [Fact]
    public void CallsAfterReleaseRunThroughTheOriginalPath()
    {
        var barrier = new TransitionPresentationBarrier();
        Assert.True(barrier.Release());

        bool deferred = barrier.TryDefer(() => Task.CompletedTask, out Task completion);

        Assert.False(deferred);
        Assert.Same(Task.CompletedTask, completion);
    }

    [Fact]
    public async Task DeferredFailureIsReportedToTheOriginalCaller()
    {
        var barrier = new TransitionPresentationBarrier();
        Assert.True(barrier.TryDefer(
            () => throw new InvalidOperationException("presentation failed"),
            out Task completion));

        barrier.Release();

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => completion);
        Assert.Equal("presentation failed", error.Message);
    }
}
