using NinjaSlayer.Code.Transition;

namespace NinjaSlayer.LogicTests;

public sealed class TransitionVideoPrewarmStateTests
{
    [Fact]
    public void DuplicateStartsAreRejectedAndCompletionIsOneShot()
    {
        var state = new TransitionVideoPrewarmState(maxAttempts: 2);

        Assert.True(state.TryBegin(out long generation));
        Assert.False(state.TryBegin(out _));
        Assert.True(state.TryMarkWarmed(generation));
        Assert.False(state.TryMarkWarmed(generation));
        Assert.Equal(TransitionVideoPrewarmPhase.Warmed, state.Phase);
    }

    [Fact]
    public void CancellationAllowsOneBoundedRetry()
    {
        var state = new TransitionVideoPrewarmState(maxAttempts: 2);

        Assert.True(state.TryBegin(out long first));
        Assert.True(state.TryReturnToIdle(first));
        Assert.True(state.TryBegin(out long second));
        Assert.NotEqual(first, second);
        Assert.True(state.TryReturnToIdle(second));
        Assert.False(state.TryBegin(out _));
        Assert.Equal(2, state.Attempts);
    }

    [Fact]
    public void CompletionFromAnObsoleteGenerationCannotWin()
    {
        var state = new TransitionVideoPrewarmState(maxAttempts: 2);

        Assert.True(state.TryBegin(out long first));
        Assert.True(state.TryReturnToIdle(first));
        Assert.True(state.TryBegin(out long second));

        Assert.False(state.TryMarkWarmed(first));
        Assert.True(state.TryMarkWarmed(second));
        Assert.Equal(TransitionVideoPrewarmPhase.Warmed, state.Phase);
    }

    [Fact]
    public void FormalPlaybackTakesOverARunningPrewarmPermanently()
    {
        var state = new TransitionVideoPrewarmState(maxAttempts: 2);
        Assert.True(state.TryBegin(out long generation));

        Assert.Equal(generation, state.BeginPlayback());
        Assert.Equal(TransitionVideoPrewarmPhase.PlaybackStarted, state.Phase);
        Assert.False(state.TryMarkWarmed(generation));
        Assert.False(state.TryReturnToIdle(generation));
        Assert.False(state.TryBegin(out _));
        Assert.Null(state.BeginPlayback());
    }
}
