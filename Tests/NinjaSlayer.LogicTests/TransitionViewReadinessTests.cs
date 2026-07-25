using NinjaSlayer.Code.Transition;

namespace NinjaSlayer.LogicTests;

public sealed class TransitionViewReadinessTests
{
    [Fact]
    public async Task ReadyCompletesEveryWaiterWithTheSameResult()
    {
        var readiness = new TransitionViewReadiness();
        Task<bool> first = readiness.WaitAsync();
        Task<bool> second = readiness.WaitAsync();

        Assert.True(readiness.TryMarkReady());

        Assert.True(await first);
        Assert.True(await second);
        Assert.Same(readiness.Completion, readiness.Completion);
    }

    [Fact]
    public async Task UnavailableUnblocksLoadingAndCannotBeOverwritten()
    {
        var readiness = new TransitionViewReadiness();

        Assert.True(readiness.TryMarkUnavailable());
        Assert.False(readiness.TryMarkReady());
        Assert.False(await readiness.Completion);
    }

    [Fact]
    public async Task CancelledWaitDoesNotCancelSharedReadiness()
    {
        var readiness = new TransitionViewReadiness();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => readiness.WaitAsync(cancellation.Token));

        Assert.True(readiness.TryMarkReady());
        Assert.True(await readiness.Completion);
    }
}
