using NinjaSlayer.Code.Transition;

namespace NinjaSlayer.LogicTests;

public sealed class TransitionCompletionProtocolTests
{
    [Fact]
    public async Task CompletionHasOneWinnerAndOneSharedResult()
    {
        var protocol = new TransitionCompletionProtocol(19);
        Assert.True(protocol.TryStart());

        int winners = 0;
        async Task<TransitionCompletionResult> CompleteAsync(int index)
        {
            if (protocol.TryBeginCompletion())
            {
                Interlocked.Increment(ref winners);
                await Task.Yield();
                protocol.Finish(new TransitionCompletionResult(
                    protocol.SessionId,
                    index % 2 == 0
                        ? TransitionCompletionStatus.Succeeded
                        : TransitionCompletionStatus.TimedOut,
                    null));
            }

            return await protocol.Completion;
        }

        TransitionCompletionResult[] results = await Task.WhenAll(
            Enumerable.Range(0, 32).Select(CompleteAsync));

        Assert.Equal(1, winners);
        Assert.All(results, result => Assert.Same(results[0], result));
        Assert.Equal(19, results[0].SessionId);
    }

    [Fact]
    public async Task RevealCanBeClaimedOnlyOnce()
    {
        var protocol = new TransitionCompletionProtocol(20);
        Assert.False(protocol.TryClaimReveal());
        Assert.True(protocol.TryStart());

        bool[] claims = await Task.WhenAll(
            Enumerable.Range(0, 32).Select(_ => Task.Run(protocol.TryClaimReveal)));

        Assert.Single(claims, claimed => claimed);
        Assert.True(protocol.TryBeginCompletion());
        Assert.False(protocol.TryClaimReveal());
        protocol.Finish(new TransitionCompletionResult(
            protocol.SessionId,
            TransitionCompletionStatus.Succeeded,
            null));
    }

    [Theory]
    [InlineData((int)TransitionCompletionStatus.Faulted)]
    [InlineData((int)TransitionCompletionStatus.Cancelled)]
    [InlineData((int)TransitionCompletionStatus.TimedOut)]
    [InlineData((int)TransitionCompletionStatus.Superseded)]
    public async Task FailurePathsConvergeOnTheSameCompletion(int statusValue)
    {
        var status = (TransitionCompletionStatus)statusValue;
        var protocol = new TransitionCompletionProtocol(21);
        Assert.True(protocol.TryStart());
        Assert.True(protocol.TryBeginCompletion());

        var expected = new TransitionCompletionResult(protocol.SessionId, status, "failure");
        protocol.Finish(expected);

        TransitionCompletionResult actual = await protocol.Completion;
        Assert.Same(expected, actual);
        Assert.False(protocol.TryBeginCompletion());
        Assert.False(protocol.TryClaimReveal());
    }
}
